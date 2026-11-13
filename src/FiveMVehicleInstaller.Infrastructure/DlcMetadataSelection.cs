using System.Xml.Linq;
using MetadataXml = FiveMVehicleInstaller.Infrastructure.Metadata;
using FiveMVehicleInstaller.Core;

namespace FiveMVehicleInstaller.Infrastructure;

internal sealed record DlcMetadataSelection(IReadOnlyDictionary<string,string> Metadata, IReadOnlyList<Diagnostic> Diagnostics)
{
    public static async Task<DlcMetadataSelection?> ReadAsync(string root, IReadOnlyList<VirtualFile> files, CancellationToken cancellationToken)
    {
        string Relative(string path) => root.Length == 0 ? path : path[(root.Length+1)..];
        var source=files.FirstOrDefault(f=>Relative(f.Path).Equals("content.xml",StringComparison.OrdinalIgnoreCase));
        if(source?.Content is null) return null;
        var xml=await MetadataXml.ReadAsync(source.Content,cancellationToken);
        var declarations=xml.Root?.Elements().FirstOrDefault(e=>e.Name.LocalName=="dataFiles")?.Elements().ToArray() ?? [];
        // Empty/missing declarations do not establish an authoritative metadata list.
        if(declarations.Length==0) return null;
        var diagnostics=new List<Diagnostic>();
        var sets=xml.Root?.Elements().FirstOrDefault(e=>e.Name.LocalName=="contentChangeSets")?.Elements().ToArray() ?? [];
        var setupSource=files.FirstOrDefault(f=>Relative(f.Path).Equals("setup2.xml",StringComparison.OrdinalIgnoreCase));
        if(setupSource?.Content is not null)
        {
            var setup=await MetadataXml.ReadAsync(setupSource.Content,cancellationToken);
            var startup=setup.Root?.Elements().FirstOrDefault(e=>e.Name.LocalName=="contentChangeSetGroups")?.Elements()
                .FirstOrDefault(e=>MetadataXml.Value(e,"NameHash").Equals("GROUP_STARTUP",StringComparison.OrdinalIgnoreCase));
            if(startup is not null)
            {
                var names=startup.Elements().FirstOrDefault(e=>e.Name.LocalName=="ContentChangeSets")?.Elements()
                    .Select(e=>e.Value.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
                foreach(var name in names.Where(n=>!sets.Any(s=>MetadataXml.Value(s,"changeSetName").Equals(n,StringComparison.OrdinalIgnoreCase))))
                    diagnostics.Add(new(DiagnosticSeverity.Warning,"Content change set de inicialização ausente: "+name+". Somente referências explicitamente ativas serão consideradas."));
                sets=sets.Where(s=>names.Contains(MetadataXml.Value(s,"changeSetName"))).ToArray();
            }
        }
        static IEnumerable<string> Values(XElement element,string list) => element.Elements()
            .Where(e=>e.Name.LocalName==list).SelectMany(e=>e.Elements()).Select(e=>e.Value.Trim());
        static string PathFor(string filename)
        {
            var separator=filename.IndexOf(":/",StringComparison.Ordinal);
            return SafePaths.NormalizeRelative((separator>=0?filename[(separator+2)..]:filename)
                .Replace("%PLATFORM%","x64",StringComparison.OrdinalIgnoreCase));
        }
        var enabled=sets.SelectMany(s=>Values(s,"filesToEnable")).Select(PathFor).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var disabled=sets.SelectMany(s=>Values(s,"filesToDisable"))
            .Concat(xml.Root is null?[]:Values(xml.Root,"disabledFiles")).Select(PathFor).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var metadata=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var declaration in declarations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var type=MetadataXml.Value(declaration,"fileType");
            var filename=MetadataXml.Value(declaration,"filename");
            if(type!="RPF_FILE" && !MetadataXml.Types.Values.Contains(type))
            {
                diagnostics.Add(new(DiagnosticSeverity.Warning,$"Referência adicional não convertida: {type}."));
                continue;
            }
            var path=PathFor(filename);
            var initiallyDisabled=declaration.Elements().FirstOrDefault(e=>e.Name.LocalName=="disabled")?.Attribute("value")?.Value;
            if(disabled.Contains(path) || (initiallyDisabled?.Equals("true",StringComparison.OrdinalIgnoreCase)==true && !enabled.Contains(path))) continue;
            var matched=files.Where(f=>Relative(f.Path).Equals(path,StringComparison.OrdinalIgnoreCase)).ToArray();
            if(type=="RPF_FILE")
            {
                var parsedContainer = matched.Any(f => f.IsContainer);
                var hasContents = files.Any(f => Relative(f.Path).StartsWith(path + "/", StringComparison.OrdinalIgnoreCase));
                if (!parsedContainer && !hasContents)
                    diagnostics.Add(new(DiagnosticSeverity.Error,"Arquivo referenciado pelo DLC não encontrado: "+path));
                else if (parsedContainer && !hasContents)
                    diagnostics.Add(new(DiagnosticSeverity.Information, "RPF referenciado encontrado e validado, sem arquivos a exportar: " + path));
                continue;
            }
            if(matched.Length==0) diagnostics.Add(new(DiagnosticSeverity.Error,"Arquivo referenciado pelo DLC não encontrado: "+path));
            foreach(var file in matched)
            {
                if(metadata.TryGetValue(file.Path,out var previous) && previous!=type)
                    diagnostics.Add(new(DiagnosticSeverity.Error,"Tipos conflitantes no DLC: "+path));
                metadata[file.Path]=type;
            }
        }
        return new(metadata,diagnostics);
    }
}