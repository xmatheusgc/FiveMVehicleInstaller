using System.Text;
using System.Xml.Linq;
using FiveMVehicleInstaller.Core;

namespace FiveMVehicleInstaller.Infrastructure;

public sealed partial class VehicleConverter : IVehicleConverter
{
    private readonly IRpfReader rpfReader;
    private readonly string temporaryParent;
    private static readonly HashSet<string> AssetExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".yft", ".ytd", ".ydr", ".ydd", ".ybn" };

    public VehicleConverter(IRpfReader? rpfReader = null, string? temporaryParent = null)
    {
        this.rpfReader = rpfReader ?? new Rpf7Reader();
        this.temporaryParent = Path.GetFullPath(temporaryParent ?? Path.Combine(Path.GetTempPath(), "FiveMVehicleInstaller"));
    }

    public Task<AnalysisSession> AnalyzeAsync(IEnumerable<string> inputs,
        IProgress<ConversionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var paths = inputs.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return Task.Run(async () =>
        {
            SafePaths.RejectReparsePoints(temporaryParent);
            Directory.CreateDirectory(temporaryParent);
            var workspace = SafePaths.Within(temporaryParent, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workspace);
            try
            {
                var packages = new List<VehiclePackage>();
                var diagnostics = new List<Diagnostic>();
                var scanner = new PackageScanner(rpfReader, workspace, progress, cancellationToken);
                foreach (var input in paths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var files = await scanner.ScanAsync(input);
                        foreach (var failure in files.Where(f => f.Error is not null))
                            diagnostics.Add(new(DiagnosticSeverity.Error, failure.Error!));
                        files = await PackOrganizer.ExpandIndexedPacksAsync(files, cancellationToken);
                        var readyRoots = files.Where(f => !f.InRpf && !f.IsManaged && (Path.GetFileName(f.Path).Equals("fxmanifest.lua",StringComparison.OrdinalIgnoreCase) || Path.GetFileName(f.Path).Equals("__resource.lua",StringComparison.OrdinalIgnoreCase))).Select(f => Parent(f.Path)).ToArray();
                        var attributionFiles = files.Where(f => !readyRoots.Any(r => Under(f.Path, r))).ToArray();
                        if (readyRoots.Length > 0) diagnostics.Add(new(DiagnosticSeverity.Information, $"{readyRoots.Length} versão(ões) FiveM ready ignorada(s). Somente DLCs em RPF, DLCs extraídos e packs do aplicativo serão analisados."));
                        var looseDlcRoots = files.Where(f => !f.InRpf && !f.IsManaged && (Path.GetFileName(f.Path).Equals("setup2.xml",StringComparison.OrdinalIgnoreCase) || Path.GetFileName(f.Path).Equals("content.xml",StringComparison.OrdinalIgnoreCase)))
                            .Select(f => Parent(f.Path)).Where(r => !readyRoots.Any(ready => r == ready || Under(r, ready))).ToArray();
                        files = files.Where(f => f.InRpf || f.IsManaged || f.Error is not null ||
                            looseDlcRoots.Any(r => Under(f.Path, r)) && !readyRoots.Any(r => Under(f.Path, r))).ToList();
                        var discovered = await DiscoverAsync(input, files, progress, cancellationToken, attributionFiles: attributionFiles);
                        foreach (var package in discovered)
                        {
                            try { packages.Add(await LayoutIsolation.ApplyAsync(package, cancellationToken)); }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                packages.Add(new VehiclePackage
                                {
                                    SourceName = package.SourceName, IsFromPack = package.IsFromPack, Credits = package.Credits,
                                    SuggestedRepair = null, SuggestedResourceName = package.SuggestedResourceName,
                                    Vehicles = package.Vehicles, Files = package.Files,
                                    Diagnostics = package.Diagnostics.Append(new Diagnostic(DiagnosticSeverity.Error,
                                        "Não foi possível isolar os layouts: " + ex.Message)).ToArray()
                                });
                            }
                        }
                        if (discovered.Count == 0)
                            diagnostics.Add(new(DiagnosticSeverity.Warning, $"{Path.GetFileName(input)}: nenhum DLC em RPF encontrado. Versões FiveM ready não são importadas; selecione o Add-On/RPF ou abra um pack criado pelo aplicativo."));
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    { diagnostics.Add(new(DiagnosticSeverity.Error, $"{input}: {ex.Message}")); }
                }
                var layoutConflicts = await LayoutCompatibility.FindConflictsAsync(packages, cancellationToken);
                if (layoutConflicts.Count > 0)
                    diagnostics.Add(new(DiagnosticSeverity.Error, LayoutCompatibility.Describe(layoutConflicts, packages)));
                for (var owner = 0; owner < packages.Count; owner++)
                {
                    var relevant = layoutConflicts.Where(c => c.Owners.Contains(owner)).ToArray();
                    if (relevant.Length == 0) continue;
                    var package = packages[owner];
                    packages[owner] = new VehiclePackage
                    {
                        SourceName = package.SourceName, IsFromPack = package.IsFromPack, Credits = package.Credits, SuggestedRepair = package.SuggestedRepair,
                        SuggestedResourceName = package.SuggestedResourceName, Vehicles = package.Vehicles, Files = package.Files,
                        Diagnostics = package.Diagnostics.Append(new Diagnostic(DiagnosticSeverity.Error, LayoutCompatibility.Describe(relevant, packages))).ToArray()
                    };
                }
                return new AnalysisSession(packages, diagnostics, () => Cleanup(workspace));
            }
            catch { Cleanup(workspace); throw; }
        }, cancellationToken);
    }

    private void Cleanup(string workspace)
    {
        try { SafePaths.DeleteOwnedDirectory(temporaryParent, workspace); }
        catch (IOException) { /* A later cleanup can remove a workspace locked by another process. */ }
        catch (UnauthorizedAccessException) { }
    }

    private static string Parent(string path) => path.Contains('/') ? path[..path.LastIndexOf('/')] : "";
    private static bool Under(string path, string root) => root.Length == 0 || path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
    private static string Relative(string path, string root) => root.Length == 0 ? path : path[(root.Length + 1)..];

    private static async Task<List<VehiclePackage>> DiscoverAsync(string input, List<VirtualFile> files,
        IProgress<ConversionProgress>? progress, CancellationToken cancellationToken, bool suggestRepairs = true,
        IReadOnlyList<VirtualFile>? attributionFiles = null)
    {
        var roots = files.Where(f => f.Content is not null &&
            (Path.GetFileName(f.Path).Equals("setup2.xml", StringComparison.OrdinalIgnoreCase) ||
             Path.GetFileName(f.Path).Equals("content.xml", StringComparison.OrdinalIgnoreCase) ||
             Path.GetFileName(f.Path).Equals("fxmanifest.lua", StringComparison.OrdinalIgnoreCase) ||
             Path.GetFileName(f.Path).Equals("__resource.lua", StringComparison.OrdinalIgnoreCase)))
            .Select(f => Parent(f.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Recognize metadata by XML root, including creators' custom filenames.
        var documents = new Dictionary<string, XDocument>(StringComparer.OrdinalIgnoreCase);
        var xmlErrors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var preparedMetadata = new Dictionary<string, PreparedMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files.Where(f => f.Content is not null && Path.GetExtension(f.Path).Equals(".meta", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var prepared = await MetadataInput.ReadAsync(file.Content!, cancellationToken);
                var document = prepared.Document;
                if (prepared.RemovedPaddingBytes > 0) preparedMetadata.Add(file.Path, prepared);
                documents.Add(file.Path, document);
                if (document.Root?.Name.LocalName == "CVehicleModelInfo__InitDataList" && !roots.Any(r => Under(file.Path, r)))
                {
                    var parent = Parent(file.Path);
                    var dataIndex = ("/" + parent + "/").LastIndexOf("/data/", StringComparison.OrdinalIgnoreCase);
                    var inferred = dataIndex >= 0 ? ("/" + parent)[..dataIndex].TrimStart('/') : parent;
                    roots.Add(inferred);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { xmlErrors[file.Path] = ex.Message; }
        }
        // A malformed vehicles.meta without setup/content must still appear as a blocked package.
        foreach (var file in files.Where(f => Path.GetFileName(f.Path).Equals("vehicles.meta", StringComparison.OrdinalIgnoreCase)))
            if (!roots.Any(r => Under(file.Path, r)))
            {
                var parent = Parent(file.Path);
                roots.Add(Path.GetFileName(parent).Equals("data", StringComparison.OrdinalIgnoreCase) ? Parent(parent) : parent);
            }
        var packages = new List<VehiclePackage>();
        foreach (var root in roots.Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new($"Analisando {root}"));
            var scoped = files.Where(f => Under(f.Path, root) && !roots.Any(other => other.Length > root.Length &&
                Under(other, root) && Under(f.Path, other)))
                .Select(f => preparedMetadata.TryGetValue(f.Path, out var prepared) ? f with { Content = prepared.Content } : f).ToArray();
            var diagnostics = new List<Diagnostic>();
            var output = new List<PackageFile>();
            var vehicles = new List<VehicleDefinition>();
            var suggested = Path.GetFileNameWithoutExtension(root.Length > 0 ? root : Path.GetFileName(input));
            DlcMetadataSelection? dlcSelection = null;
            if (!scoped.Any(f => f.IsManaged))
            {
                try
                {
                    dlcSelection = await DlcMetadataSelection.ReadAsync(root, scoped, cancellationToken);
                    if (dlcSelection is not null) diagnostics.AddRange(dlcSelection.Diagnostics);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { diagnostics.Add(new(DiagnosticSeverity.Error, "Referências do DLC inválidas: " + ex.Message)); }
            }
            foreach (var file in scoped)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Relative(file.Path, root);
                if (file.Error is not null) { diagnostics.Add(new(DiagnosticSeverity.Error, file.Error)); continue; }
                if (file.Content is null || file.IsContainer) continue;
                var name = Path.GetFileName(file.Path);
                if (name.Equals("fxmanifest.lua", StringComparison.OrdinalIgnoreCase) || name.Equals("__resource.lua", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Equals("setup2.xml", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var setup = await Metadata.ReadAsync(file.Content, cancellationToken);
                        var hash = setup.Root is null ? "" : Metadata.Value(setup.Root, "nameHash");
                        if (hash.Length > 0) suggested = hash;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    { diagnostics.Add(new(DiagnosticSeverity.Error, $"{relative}: {ex.Message}")); }
                    continue;
                }
                if (name.Equals("content.xml", StringComparison.OrdinalIgnoreCase)) continue;
                if (dlcSelection is not null && Path.GetExtension(file.Path).Equals(".meta", StringComparison.OrdinalIgnoreCase) &&
                    !dlcSelection.Metadata.ContainsKey(file.Path))
                {
                    diagnostics.Add(new(DiagnosticSeverity.Information, "Metadado não ativado pelo DLC, não exportado: " + relative));
                    continue;
                }
                if (xmlErrors.TryGetValue(file.Path, out var error))
                { diagnostics.Add(new(DiagnosticSeverity.Error, $"XML inválido em {relative}: {error}")); continue; }
                if (documents.TryGetValue(file.Path, out var document) && document.Root is not null &&
                    Metadata.Types.TryGetValue(document.Root.Name.LocalName, out var dataType))
                {
                    if (dlcSelection is not null && dlcSelection.Metadata.TryGetValue(file.Path, out var dlcType) && dlcType != dataType)
                        diagnostics.Add(new(DiagnosticSeverity.Error, $"Tipo do DLC não corresponde ao XML: {relative} ({dlcType}, esperado {dataType})."));
                    var dataRelative = relative.StartsWith("data/", StringComparison.OrdinalIgnoreCase) ? relative[5..] : relative;
                    output.Add(new(file.Path, "data/" + dataRelative, dataType, file.Content));
                    if (preparedMetadata.TryGetValue(file.Path, out var normalized))
                        diagnostics.Add(new(DiagnosticSeverity.Information,
                            $"Compatibilidade XML: {normalized.RemovedPaddingBytes} byte(s) de preenchimento NUL após o documento ignorados na cópia: {relative}. Conteúdo XML e arquivo de entrada preservados."));
                    if (dataType == "VEHICLE_METADATA_FILE")
                    {
                        var items = Metadata.VehicleItems(document);
                        for (var i = 0; i < items.Length; i++)
                        {
                            var item = items[i];
                            vehicles.Add(new($"{file.Path}#{i}", file.Path, i, Metadata.Value(item, "modelName"),
                                Metadata.Value(item, "txdName"), Metadata.Value(item, "handlingId"), Metadata.Value(item, "audioNameHash")));
                        }
                    }
                }
                else if (AssetExtensions.Contains(Path.GetExtension(file.Path)))
                {
                    // DLC/RPF directories are packaging details; stream uses the original asset filenames.
                    var assetRelative = Path.GetFileName(relative);
                    output.Add(new(file.Path, "stream/" + assetRelative, null, file.Content));
                }
                else diagnostics.Add(new(DiagnosticSeverity.Information, $"Não exportado: {relative}"));
            }
            if (dlcSelection is not null)
                foreach (var reference in dlcSelection.Metadata.Keys.Where(path => !output.Any(f => f.SourcePath == path)))
                    diagnostics.Add(new(DiagnosticSeverity.Error, "Metadado ativo do DLC não pôde ser convertido: " + Relative(reference, root)));
            await HandlingCompatibility.ValidateAsync(output, diagnostics, cancellationToken);
            if (vehicles.Count == 0) diagnostics.Add(new(DiagnosticSeverity.Error, "Pacote sem definição de veículo válida."));
            foreach (var vehicle in vehicles)
            {
                if (vehicle.ModelName.Length == 0 || !output.Any(f => f.DataFileType is null &&
                    Path.GetFileName(f.DestinationPath).Equals(vehicle.ModelName + ".yft", StringComparison.OrdinalIgnoreCase)))
                    diagnostics.Add(new(DiagnosticSeverity.Error, $"Modelo principal ausente: {vehicle.ModelName}.yft"));
            }
            foreach (var group in output.GroupBy(f => f.DestinationPath, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                diagnostics.Add(new(DiagnosticSeverity.Error, $"Caminho de saída duplicado: {group.Key}"));
            // GTA identifies streamed assets by name even when they live in distinct directories.
            foreach (var group in output.Where(f => f.DataFileType is null)
                .GroupBy(f => Path.GetFileName(f.DestinationPath), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                diagnostics.Add(new(DiagnosticSeverity.Error, $"Nome de asset conflitante: {group.Key}"));
            foreach (var group in vehicles.GroupBy(v => v.ModelName, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                diagnostics.Add(new(DiagnosticSeverity.Error, $"Modelo definido mais de uma vez: {group.Key}"));
            PackageRepairProposal? repair = null;
            if (suggestRepairs && diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                foreach (var invalid in scoped.Where(f => xmlErrors.ContainsKey(f.Path) && f.Content is not null))
                {
                    var corrected = await KnownMetadataRepairs.TryCorrectVisibleModsAsync(invalid.Content!, cancellationToken);
                    if (corrected is null) continue;
                    var revisedFiles = scoped.Select(f => f.Path == invalid.Path ? f with { Content = corrected } : f).ToList();
                    var revised = await DiscoverAsync(input, revisedFiles, null, cancellationToken, suggestRepairs: false, attributionFiles: attributionFiles);
                    if (revised.Count != 1 || !revised[0].CanExport) continue;
                    repair = new("Trocar </linkMods> por </visibleMods> em " + Relative(invalid.Path, root) +
                        ". Somente essa tag será alterada na cópia; o arquivo de entrada será preservado.", revised[0]);
                    break;
                }
            }
            packages.Add(new VehiclePackage
            {
                SuggestedRepair = repair,
                SourceName = Path.GetFileName(input) + (root.Length > 0 ? " / " + root : ""),
                IsFromPack = scoped.Any(f => f.IsManaged),
                Credits = scoped.Any(f => f.IsManaged) ? scoped.SelectMany(f => f.Credits ?? []).Distinct().ToArray() :
                    await ModAttribution.DetectAsync(input, root, roots, attributionFiles ?? files, cancellationToken),
                SuggestedResourceName = SafePaths.SuggestResourceName(suggested), Vehicles = vehicles,
                Files = output.OrderBy(f => f.DestinationPath, StringComparer.OrdinalIgnoreCase).ToArray(), Diagnostics = diagnostics
            });
        }
        return packages;
    }

    public Task<ExportResult> ExportPackAsync(IReadOnlyList<PackageExportRequest> packages, PackExportOptions options,
        IProgress<ConversionProgress>? progress = null, CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        SafePaths.ValidateResourceName(options.ResourceName);
        var validation = await ValidatePackAsync(packages,cancellationToken);
        if(!validation.CanExport)throw new InvalidDataException(string.Join("\n",validation.Issues.Select(i=>i.Message+": "+string.Join(", ",i.Packages))));
        var composed = await PackOrganizer.ComposeAsync(packages, progress, cancellationToken);
        return await ExportAsync(composed.Package,
            new(options.ResourceName, options.DestinationDirectory, composed.AudioOverrides), progress, cancellationToken);
    }, cancellationToken);

    public Task<ExportResult> ExportAsync(VehiclePackage package, ExportOptions options,
        IProgress<ConversionProgress>? progress = null, CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        if (!package.CanExport) throw new InvalidOperationException("O pacote contém erros e não pode ser exportado.");
        var name = SafePaths.ValidateResourceName(options.ResourceName);
        var parent = Path.GetFullPath(options.DestinationDirectory);
        SafePaths.RejectReparsePoints(parent);
        var destination = SafePaths.Within(parent, name);
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException("O destino já existe. Escolha outro nome para o resource.");
        var unknown = options.AudioOverrides.Keys.Except(package.Vehicles.Select(v => v.Key)).ToArray();
        if (unknown.Length > 0) throw new InvalidDataException("Alteração de som referencia veículo desconhecido.");
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(parent);
        var staging = SafePaths.Within(parent, ".fv-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var completed = 0;
            foreach (var file in package.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new($"Exportando {file.DestinationPath}", completed, package.Files.Count));
                var target = SafePaths.Within(staging, file.DestinationPath);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                if (file.DataFileType == "VEHICLE_METADATA_FILE")
                    await Metadata.WriteAudioAsync(file, package, options.AudioOverrides, stream, cancellationToken);
                else
                {
                    await using var original = await file.Content.OpenReadAsync(cancellationToken);
                    await original.CopyToAsync(stream, cancellationToken);
                }
                completed++;
            }
            await File.WriteAllTextAsync(SafePaths.Within(staging, "fxmanifest.lua"), CreateManifest(package), new UTF8Encoding(false), cancellationToken);
            if (!package.Files.Any(f => f.DestinationPath == PackOrganizer.IndexName))
            {
                var map = package.Files.Select(f => new PackFileMap(f.DestinationPath, f.DestinationPath, f.DataFileType)).ToArray();
                var index = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new PackIndex(1, [new PackGroup(name, map, package.Credits)]));
                await File.WriteAllBytesAsync(SafePaths.Within(staging, PackOrganizer.IndexName), index, cancellationToken);
            }
            var report = new StringBuilder().AppendLine($"Origem: {package.SourceName}").AppendLine($"Resource: {name}")
                .AppendLine($"Arquivos: {package.Files.Count}").AppendLine($"ensure {name}");
            ModAttribution.AppendReport(report, package);
            foreach (var diagnostic in package.Diagnostics) report.AppendLine($"[{diagnostic.Severity}] {diagnostic.Message}");
            foreach (var vehicle in package.Vehicles)
                report.AppendLine($"{vehicle.ModelName}: audioNameHash = {options.AudioOverrides.GetValueOrDefault(vehicle.Key, vehicle.AudioNameHash)}");
            await File.WriteAllTextAsync(SafePaths.Within(staging, "relatorio.txt"), report.ToString(), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            SafePaths.RejectReparsePoints(parent);
            // Both resolved paths belong to the selected destination; move never overwrites.
            await CommitDirectoryAsync(parent, staging, name, cancellationToken);
            progress?.Report(new($"Concluído: ensure {name}", package.Files.Count, package.Files.Count));
            return new ExportResult(destination, $"ensure {name}", package.Files.Count);
        }
        finally { if (Directory.Exists(staging)) SafePaths.DeleteOwnedDirectory(parent, staging); }
    }, cancellationToken);

    private static async Task CommitDirectoryAsync(string parent, string staging, string name, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SafePaths.RejectReparsePoints(parent);
            var source = SafePaths.Within(parent, Path.GetFileName(staging));
            var destination = SafePaths.Within(parent, name);
            SafePaths.RejectReparsePoints(source);
            try { Directory.Move(source, destination); return; }
            catch (IOException ex) when (OperatingSystem.IsWindows() && (ex.HResult & 0xffff) is 5 or 32 or 33 &&
                attempt < 4 && Directory.Exists(source) && !Directory.Exists(destination) && !File.Exists(destination))
            {
                // Indexers and scanners can briefly hold a just-written directory without delete sharing.
                // Retry only the final rename; never rewrite files or overwrite an existing destination.
                await Task.Delay(60 << attempt, cancellationToken);
            }
        }
    }
    private static string Quote(string value) => "'" + value.Replace("\\", "\\\\").Replace("'", "\\'") + "'";
    internal static string CreateManifest(VehiclePackage package)
    {
        // Register vehicle definitions before modkits, and modkits before variations.
        // FiveM preserves their manifest order; alphabetical filenames are not a load order.
        var metadata = package.Files.Where(f => f.DataFileType is not null)
            .OrderBy(f => f.DataFileType switch
            {
                "HANDLING_FILE" => 0,
                "VEHICLE_LAYOUTS_FILE" => 1,
                "VEHICLE_METADATA_FILE" => 2,
                "CARCOLS_FILE" => 3,
                "VEHICLE_VARIATION_FILE" => 4,
                _ => 5
            })
            .ThenBy(f => f.DestinationPath, StringComparer.OrdinalIgnoreCase).ToArray();
        var manifest = new StringBuilder("fx_version 'cerulean'\ngame 'gta5'\n\nfiles {\n");
        foreach (var file in metadata) manifest.AppendLine($"    {Quote(file.DestinationPath)},");
        manifest.AppendLine("}").AppendLine();
        foreach (var file in metadata) manifest.AppendLine($"data_file {Quote(file.DataFileType!)} {Quote(file.DestinationPath)}");
        return manifest.ToString();
    }
}
