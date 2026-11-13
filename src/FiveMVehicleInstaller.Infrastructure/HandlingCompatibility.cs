using System.Xml.Linq;
using FiveMVehicleInstaller.Core;

namespace FiveMVehicleInstaller.Infrastructure;

internal static class HandlingCompatibility
{
    public static async Task ValidateAsync(IReadOnlyList<PackageFile> files, List<Diagnostic> diagnostics, CancellationToken cancellationToken)
    {
        var definitions=new Dictionary<string,(string Path,XElement Definition)>(StringComparer.OrdinalIgnoreCase);
        foreach(var file in files.Where(f=>f.DataFileType=="HANDLING_FILE"))
        {
            var xml=await Metadata.ReadAsync(file.Content,cancellationToken);
            foreach(var item in xml.Root?.Elements().Where(e=>e.Name.LocalName=="HandlingData").SelectMany(e=>e.Elements()) ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name=Metadata.Value(item,"handlingName"); if(name.Length==0)continue;
                var definition=new XElement(item);
                foreach(var node in definition.DescendantNodes().Where(n=>n is XComment || n is XText t && string.IsNullOrWhiteSpace(t.Value)).ToArray())node.Remove();
                if(definitions.TryGetValue(name,out var previous) && !XNode.DeepEquals(previous.Definition,definition))
                    diagnostics.Add(new(DiagnosticSeverity.Error,$"Handling conflitante {name}: {previous.Path} e {file.DestinationPath}. Mantenha apenas a configuração ativa; alternativas não devem ser carregadas juntas. Os metadados não serão alterados."));
                else definitions[name]=(file.DestinationPath,definition);
            }
        }
    }
}