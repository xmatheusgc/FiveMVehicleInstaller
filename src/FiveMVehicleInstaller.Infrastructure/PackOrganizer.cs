using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using FiveMVehicleInstaller.Core;

namespace FiveMVehicleInstaller.Infrastructure;

internal sealed class MemoryContentSource(byte[] bytes) : IContentSource
{
    public long Length => bytes.Length;
    public ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<Stream>(new MemoryStream(bytes, false));
    }
}

internal sealed record PackFileMap(string PackPath, string OriginalPath, string? DataFileType);
internal sealed record PackGroup(string ResourceName, IReadOnlyList<PackFileMap> Files, IReadOnlyList<ModCredit>? Credits = null);
internal sealed record PackIndex(int Version, IReadOnlyList<PackGroup> Packages);
internal sealed record ComposedPack(VehiclePackage Package, IReadOnlyDictionary<string, string> AudioOverrides);

internal static class PackOrganizer
{
    public const string IndexName = ".fvpack.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static readonly HashSet<string> AssetExtensions = new(StringComparer.OrdinalIgnoreCase) { ".yft", ".ytd", ".ydr", ".ydd", ".ybn" };

    public static async Task<ComposedPack> ComposeAsync(IReadOnlyList<PackageExportRequest> requests,
        IProgress<ConversionProgress>? progress, CancellationToken cancellationToken)
    {
        if (requests.Count == 0) throw new InvalidDataException("Selecione pelo menos um pacote para montar o pack.");
        var packages = requests.Select(r => r.Package).ToArray();
        var layoutConflicts = await LayoutCompatibility.FindConflictsAsync(packages, cancellationToken);
        if (layoutConflicts.Count > 0) throw new InvalidDataException("O pack tem " + LayoutCompatibility.Describe(layoutConflicts, packages));
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var models = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var assets = new Dictionary<string, PackageFile>(StringComparer.OrdinalIgnoreCase);
        var kitIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var kitNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var handling = new Dictionary<string, (XElement Definition, string Owner)>(StringComparer.OrdinalIgnoreCase);
        var output = new List<PackageFile>(); var vehicles = new List<VehicleDefinition>();
        var groups = new List<PackGroup>(); var diagnostics = new List<Diagnostic>();
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!request.Package.CanExport) throw new InvalidDataException("Pacote com erros: " + request.ResourceName);
            var name = SafePaths.ValidateResourceName(request.ResourceName);
            if (!names.Add(name)) throw new InvalidDataException("Nomes de pacote repetidos: " + name);
            if (request.AudioOverrides.Keys.Except(request.Package.Vehicles.Select(v => v.Key)).Any())
                throw new InvalidDataException("Alteração de som referencia veículo desconhecido: " + name);
            progress?.Report(new("Verificando conflitos em " + name));
            foreach (var vehicle in request.Package.Vehicles)
            {
                if (!models.Add(vehicle.ModelName)) throw new InvalidDataException("Modelo repetido no pack: " + vehicle.ModelName);
                var key = name + "/" + vehicle.Key;
                vehicles.Add(vehicle with { Key = key, MetadataPath = name + "/" + vehicle.MetadataPath });
                if (request.AudioOverrides.TryGetValue(vehicle.Key, out var sound)) overrides[key] = sound;
            }
            foreach (var file in request.Package.Files.Where(f => f.DataFileType is "CARCOLS_FILE" or "HANDLING_FILE"))
            {
                var xml = await Metadata.ReadAsync(file.Content, cancellationToken);
                if (file.DataFileType == "CARCOLS_FILE")
                {
                    foreach (var kit in xml.Root?.Elements().Where(e => e.Name.LocalName == "Kits").SelectMany(e => e.Elements()) ?? [])
                    {
                        var id = kit.Elements().FirstOrDefault(e => e.Name.LocalName == "id")?.Attribute("value")?.Value;
                        if (id is not null)
                        {
                            if (!uint.TryParse(id, out var numericId)) throw new InvalidDataException("ID de modkit inválido em " + name);
                            CheckOwner(kitIds, numericId.ToString(System.Globalization.CultureInfo.InvariantCulture), name, "ID de modkit");
                        }
                        var kitName = Metadata.Value(kit, "kitName");
                        if (kitName.Length > 0) CheckOwner(kitNames, kitName, name, "Nome de modkit");
                    }
                }
                else
                {
                    foreach (var item in xml.Root?.Elements().Where(e => e.Name.LocalName == "HandlingData").SelectMany(e => e.Elements()) ?? [])
                    {
                        var id = Metadata.Value(item, "handlingName");
                        if (id.Length == 0) continue;
                        if (handling.TryGetValue(id, out var existing) && existing.Owner != name && !XNode.DeepEquals(WithoutWhitespace(existing.Definition), WithoutWhitespace(item)))
                            throw new InvalidDataException($"Handling conflitante {id}: {existing.Owner} e {name}. Os IDs não serão alterados automaticamente.");
                        handling[id] = (item, name);
                    }
                }
            }
            var map = new List<PackFileMap>();
            foreach (var file in request.Package.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var original = SafePaths.NormalizeRelative(file.DestinationPath);
                var prefix = file.DataFileType is null ? "stream/" : "data/";
                if (!original.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Arquivo fora da estrutura de veículo suportada: " + original);
                var packed = prefix + name + "/" + (file.DataFileType is null ? Path.GetFileName(original) : original[prefix.Length..]);
                var candidate = new PackageFile(name + "/" + file.SourcePath, packed, file.DataFileType, file.Content);
                if (file.DataFileType is null)
                {
                    if (!AssetExtensions.Contains(Path.GetExtension(original))) throw new InvalidDataException("Asset não suportado: " + original);
                    var assetName = Path.GetFileName(original);
                    if (assets.TryGetValue(assetName, out var existing))
                    {
                        if (!await IdenticalAsync(existing.Content, file.Content, cancellationToken))
                            throw new InvalidDataException($"Asset conflitante: {assetName}. Pastas diferentes não evitam conflitos de nomes no GTA.");
                        packed = existing.DestinationPath;
                        diagnostics.Add(new(DiagnosticSeverity.Information, "Asset compartilhado incluído uma vez: " + assetName));
                    }
                    else { assets.Add(assetName, candidate); output.Add(candidate); }
                }
                else output.Add(candidate);
                map.Add(new(packed, original, file.DataFileType));
            }
            groups.Add(new(name, map, request.Package.Credits));
            foreach (var credit in request.Package.Credits)
                diagnostics.Add(new(DiagnosticSeverity.Information, $"{name}: autoria — {credit.Name} ({credit.Role}); fonte: {credit.SourcePath}; trecho: {credit.Evidence}"));
            diagnostics.AddRange(request.Package.Diagnostics.Select(d => d with { Message = name + ": " + d.Message }));
        }
        var index = JsonSerializer.SerializeToUtf8Bytes(new PackIndex(1, groups), JsonOptions);
        output.Add(new(IndexName, IndexName, null, new MemoryContentSource(index)));
        diagnostics.Add(new(DiagnosticSeverity.Information, $"Pack com {groups.Count} origens. Importe-o novamente para exportar os pacotes separados."));
        return new(new VehiclePackage
        {
            SourceName = "União de " + groups.Count + " pacotes", SuggestedResourceName = "pack_veiculos",
            Credits = packages.SelectMany(p => p.Credits).Distinct().ToArray(),
            Vehicles = vehicles, Files = output, Diagnostics = diagnostics
        }, overrides);
    }

    private static XElement WithoutWhitespace(XElement element)
    {
        var copy = new XElement(element);
        foreach (var whitespace in copy.DescendantNodes().OfType<XText>().Where(t => string.IsNullOrWhiteSpace(t.Value)).ToArray()) whitespace.Remove();
        return copy;
    }
    private static void CheckOwner(Dictionary<string, string> owners, string value, string name, string label)
    {
        if (owners.TryGetValue(value, out var previous) && previous != name)
            throw new InvalidDataException($"{label} repetido ({value}): {previous} e {name}. Os metadados serão preservados; resolva o conflito nas entradas antes de unir.");
        owners[value] = name;
    }
    private static async Task<bool> IdenticalAsync(IContentSource a, IContentSource b, CancellationToken cancellationToken)
    {
        if (a.Length != b.Length) return false;
        await using var left = await a.OpenReadAsync(cancellationToken);
        var hashA = await SHA256.HashDataAsync(left, cancellationToken);
        await using var right = await b.OpenReadAsync(cancellationToken);
        var hashB = await SHA256.HashDataAsync(right, cancellationToken);
        return hashA.AsSpan().SequenceEqual(hashB);
    }

    public static async Task<List<VirtualFile>> ExpandIndexedPacksAsync(List<VirtualFile> files, CancellationToken cancellationToken)
    {
        foreach (var marker in files.Where(f => Path.GetFileName(f.Path).Equals(IndexName, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = marker.Path.Contains('/') ? marker.Path[..marker.Path.LastIndexOf('/')] : "";
            var scoped = files.Where(f => Under(f.Path, root)).ToArray();
            if (marker.Content is null || marker.Content.Length > 16 * 1024 * 1024) throw new InvalidDataException("Índice de pack inválido ou muito grande.");
            await using var stream = await marker.Content.OpenReadAsync(cancellationToken);
            var index = await JsonSerializer.DeserializeAsync<PackIndex>(stream, JsonOptions, cancellationToken);
            if (index is null || index.Version != 1 || index.Packages is null || index.Packages.Count is 0 or > 10_000)
                throw new InvalidDataException("Versão ou estrutura do índice do pack não suportada.");
            var manifest = scoped.SingleOrDefault(f => Relative(f.Path, root).Equals("fxmanifest.lua", StringComparison.OrdinalIgnoreCase));
            if (manifest?.Content is null) throw new InvalidDataException("Pack sem fxmanifest.lua.");
            var inspection = await ResourceManifestReader.InspectAsync(manifest, root, scoped, cancellationToken);
            if (inspection.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                throw new InvalidDataException(string.Join("; ", inspection.Diagnostics.Select(d => d.Message)));
            var byPath = scoped.ToDictionary(f => Relative(f.Path, root), StringComparer.OrdinalIgnoreCase);
            var mapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var groupNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var restored = new List<VirtualFile>();
            var mappedCount = 0;
            foreach (var group in index.Packages)
            {
                if (group is null || group.Files is null || group.Files.Count == 0) throw new InvalidDataException("Pacote vazio no índice.");
                var name = SafePaths.ValidateResourceName(group.ResourceName);
                if (!groupNames.Add(name)) throw new InvalidDataException("Pacotes repetidos no índice: " + name);
                var groupRoot = (root.Length == 0 ? "" : root + "/") + ".fv-packages/" + name;
                var originals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var map in group.Files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (map is null || ++mappedCount > 100_000) throw new InvalidDataException("Índice de pack excede o limite de arquivos.");
                    var packed = SafePaths.NormalizeRelative(map.PackPath);
                    var original = SafePaths.NormalizeRelative(map.OriginalPath);
                    var prefix = map.DataFileType is null ? "stream/" : "data/";
                    if (!packed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !original.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !originals.Add(original))
                        throw new InvalidDataException("Caminho inválido ou repetido no índice: " + original);
                    if (map.DataFileType is null ? !AssetExtensions.Contains(Path.GetExtension(original)) :
                        !Metadata.Types.Values.Contains(map.DataFileType) || !inspection.Metadata.TryGetValue((root.Length == 0 ? "" : root + "/") + packed, out var type) || type != map.DataFileType)
                        throw new InvalidDataException("Tipo de arquivo inconsistente no índice: " + original);
                    if (!byPath.TryGetValue(packed, out var actual) || actual.Content is null)
                        throw new InvalidDataException("Arquivo do pack ausente: " + packed);
                    mapped.Add(packed);
                    restored.Add(new(groupRoot + "/" + original, actual.Content, IsManaged: true));
                }
                var setup = Encoding.UTF8.GetBytes("<SSetupData><nameHash>" + name + "</nameHash></SSetupData>");
                if (group.Credits is { } credits && (credits.Count > 128 || credits.Any(c => c is null ||
                    string.IsNullOrWhiteSpace(c.Name) || c.Name.Length > 100 || c.Role is null || c.Role.Length > 100 ||
                    c.SourcePath is null || c.SourcePath.Length > 32768 || c.Evidence is null || c.Evidence.Length > 600 ||
                    !Enum.IsDefined(c.Confidence) || new[] { c.Name, c.Role, c.SourcePath, c.Evidence }.Any(s => s.Any(char.IsControl)))))
                    throw new InvalidDataException("Créditos inválidos no índice do pack: " + name);
                restored.Add(new(groupRoot + "/setup2.xml", new MemoryContentSource(setup), IsManaged: true, Credits: group.Credits));
            }
            foreach (var file in scoped)
            {
                var relative = Relative(file.Path, root);
                if (file.Error is not null || (AssetExtensions.Contains(Path.GetExtension(relative)) || inspection.Metadata.ContainsKey(file.Path)) && !mapped.Contains(relative))
                    throw new InvalidDataException("Arquivo fora do índice; não é seguro separar o pack: " + relative);
            }
            files.RemoveAll(f => Under(f.Path, root));
            files.AddRange(restored);
        }
        return files;
    }
    private static bool Under(string path, string root) => root.Length == 0 || path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
    private static string Relative(string path, string root) => root.Length == 0 ? path : path[(root.Length + 1)..];
}
