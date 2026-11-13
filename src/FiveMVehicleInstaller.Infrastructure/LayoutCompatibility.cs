using System.Xml.Linq;
using FiveMVehicleInstaller.Core;

namespace FiveMVehicleInstaller.Infrastructure;

internal sealed record LayoutConflict(string Section, string Name, IReadOnlyList<int> Owners)
{
    public string Message(IReadOnlyList<VehiclePackage> packages, bool includeAdvice = true) =>
        $"Definição de layout conflitante {Section}/{Name}: {string.Join("; ", Owners.Select(i => string.Join(", ", packages[i].Vehicles.Select(v => v.ModelName))).Distinct(StringComparer.OrdinalIgnoreCase))}." +
        (includeAdvice ? " " + LayoutCompatibility.Advice : "");
}

internal static class LayoutCompatibility
{
    public const string Advice = "Bancos, portas ou animações ainda possuem definições incompatíveis após o isolamento. Revise as definições de origem antes de exportar.";
    public static string Describe(IReadOnlyList<LayoutConflict> conflicts, IReadOnlyList<VehiclePackage> packages) =>
        $"{conflicts.Count} definição(ões) de layout conflitante:\n" +
        string.Join("\n", conflicts.Select(c => c.Message(packages, includeAdvice: false))) + "\n" + Advice;
    public static async Task<IReadOnlyList<LayoutConflict>> FindConflictsAsync(
        IReadOnlyList<VehiclePackage> packages, CancellationToken cancellationToken)
    {
        var definitions = new Dictionary<(string Section, string Name), List<(int Owner, XElement Xml)>>();
        for (var owner = 0; owner < packages.Count; owner++)
        {
            foreach (var file in packages[owner].Files.Where(f => f.DataFileType == "VEHICLE_LAYOUTS_FILE"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var xml = await Metadata.ReadAsync(file.Content, cancellationToken);
                foreach (var section in xml.Root?.Elements() ?? [])
                foreach (var item in section.Elements().Where(e => e.Name.LocalName == "Item"))
                {
                    var name = Metadata.Value(item, "Name");
                    if (name.Length == 0) continue;
                    var key = (section.Name.LocalName.ToUpperInvariant(), name.ToUpperInvariant());
                    if (!definitions.TryGetValue(key, out var entries)) definitions[key] = entries = [];
                    entries.Add((owner, Normalize(item)));
                }
            }
        }
        return definitions.Where(pair => pair.Value.Skip(1).Any(entry => !XNode.DeepEquals(pair.Value[0].Xml, entry.Xml)))
            .Select(pair => new LayoutConflict(pair.Key.Section,
                Metadata.Value(pair.Value[0].Xml, "Name"), pair.Value.Select(d => d.Owner).Distinct().ToArray())).ToArray();
    }

    private static XElement Normalize(XElement source)
    {
        var copy = new XElement(source);
        foreach (var node in copy.DescendantNodes().Where(n => n is XComment or XProcessingInstruction ||
            n is XText text && string.IsNullOrWhiteSpace(text.Value)).ToArray()) node.Remove();
        foreach (var element in copy.DescendantsAndSelf())
        {
            var attributes = element.Attributes().OrderBy(a => a.Name.ToString(), StringComparer.Ordinal).ToArray();
            element.RemoveAttributes();
            element.Add(attributes);
        }
        return copy;
    }
}