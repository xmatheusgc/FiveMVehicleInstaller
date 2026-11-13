using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using FiveMVehicleInstaller.Core;

namespace FiveMVehicleInstaller.Infrastructure;

// Isolate definitions owned by each DLC, even when it is imported on its own.
// Model names are stable across resource renames, pack composition and reopening.
internal static class LayoutIsolation
{
    private static readonly HashSet<string> VehicleReferences = new(StringComparer.Ordinal)
        { "layout", "coverBoundOffsets", "explosionInfo", "scenarioLayout" };
    private static readonly HashSet<string> SeatReferences = new(StringComparer.Ordinal)
        { "ShuffleLink", "RearSeatLink" };

    public static async Task<VehiclePackage> ApplyAsync(VehiclePackage package, CancellationToken ct)
    {
        if (package.SuggestedRepair is { } proposal)
        {
            package = new VehiclePackage
            {
                SourceName = package.SourceName, IsFromPack = package.IsFromPack, Credits = package.Credits,
                SuggestedResourceName = package.SuggestedResourceName, Vehicles = package.Vehicles,
                Files = package.Files, Diagnostics = package.Diagnostics,
                SuggestedRepair = proposal with { RepairedPackage = await ApplyAsync(proposal.RepairedPackage, ct) }
            };
        }
        if (!package.CanExport) return package;
        var documents = new Dictionary<PackageFile, XDocument>();
        foreach (var file in package.Files.Where(f => f.DataFileType is "VEHICLE_LAYOUTS_FILE" or "VEHICLE_METADATA_FILE"))
        {
            await using var input = await file.Content.OpenReadAsync(ct);
            documents[file] = await Metadata.ReadDocumentAsync(input,
                LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo, ct);
        }
        var identity = string.Join("\n", package.Vehicles.Select(v => v.ModelName.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        var prefix = "FVLI_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16] + "_";
        var definitions = documents.Where(d => d.Key.DataFileType == "VEHICLE_LAYOUTS_FILE")
            .SelectMany(d => d.Value.Root?.Elements().SelectMany(s => s.Elements()) ?? [])
            .SelectMany(item => item.Elements().Where(e => e.Name.LocalName == "Name"))
            .Where(e => e.Value.Trim().Length > 0).ToArray();
        var names = definitions.Select(e => e.Value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var mapping = names.Where(n => !n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(n => n, n => prefix + n, StringComparer.OrdinalIgnoreCase);
        if (mapping.Count == 0) return package;
        // Do not merge a source definition with an existing isolated definition.
        if (mapping.Values.Any(value => names.Contains(value, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidDataException("Identificadores de layout originais e isolados se sobrepõem no mesmo pacote.");
        var updated = new Dictionary<PackageFile, PackageFile>();
        foreach (var (file, document) in documents)
        {
            ct.ThrowIfCancellationRequested();
            await using var input = await file.Content.OpenReadAsync(ct);
            using var original = new MemoryStream();
            await input.CopyToAsync(original, ct);
            var patcher = new XmlBytePatcher(original.ToArray(), document.Declaration?.Encoding);
            var changes = 0;
            foreach (var element in document.Descendants())
            {
                var layoutFile = file.DataFileType == "VEHICLE_LAYOUTS_FILE";
                var isDefinition = layoutFile && definitions.Contains(element);
                var isTextReference = layoutFile ? SeatReferences.Contains(element.Name.LocalName) :
                    VehicleReferences.Contains(element.Name.LocalName) &&
                    element.Parent?.Name.LocalName == "Item" && element.Parent.Parent?.Name.LocalName == "InitDatas";
                if (!element.HasElements && (isDefinition || isTextReference) &&
                    mapping.TryGetValue(element.Value.Trim(), out var replacement))
                {
                    patcher.ReplaceValue(element, replacement);
                    changes++;
                }
                if (!layoutFile) continue;
                foreach (var attribute in element.Attributes().Where(a => a.Name.LocalName == "ref"))
                    if (mapping.TryGetValue(attribute.Value.Trim(), out var reference))
                    {
                        patcher.ReplaceAttribute(attribute, reference);
                        changes++;
                    }
            }
            if (changes == 0) continue;
            using var output = new MemoryStream();
            await patcher.WriteAsync(output, ct);
            var content = new MemoryContentSource(output.ToArray());
            await Metadata.ReadAsync(content, ct); // Validate generated XML before exposing it to export.
            updated[file] = file with { Content = content };
        }
        return new VehiclePackage
        {
            SourceName = package.SourceName, IsFromPack = package.IsFromPack, Credits = package.Credits,
            SuggestedRepair = package.SuggestedRepair, SuggestedResourceName = package.SuggestedResourceName,
            Vehicles = package.Vehicles, Files = package.Files.Select(f => updated.GetValueOrDefault(f, f)).ToArray(),
            Diagnostics = package.Diagnostics.Append(new Diagnostic(DiagnosticSeverity.Information,
                $"Layouts isolados por pacote: {mapping.Count} identificador(es). Referências locais atualizadas; definições nativas e arquivos de entrada preservados.")).ToArray()
        };
    }
}
