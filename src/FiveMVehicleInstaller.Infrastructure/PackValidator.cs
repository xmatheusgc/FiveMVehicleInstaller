using System.Security.Cryptography;
using System.Xml.Linq;
using FiveMVehicleInstaller.Core;

namespace FiveMVehicleInstaller.Infrastructure;

internal static class PackValidator
{
    public static async Task<PackValidationResult> ValidateAsync(IReadOnlyList<PackageExportRequest> requests, CancellationToken ct)
    {
        var issues = new List<PackIssue>();
        void Add(string message, IEnumerable<string> owners) => issues.Add(new(message, owners.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()));
        if (requests.Count == 0) Add("Selecione pelo menos um pacote.", []);
        foreach (var request in requests)
        {
            ct.ThrowIfCancellationRequested();
            if(request.AudioOverrides.Keys.Except(request.Package.Vehicles.Select(v=>v.Key)).Any()) Add("Alteração de som referencia veículo desconhecido: "+request.ResourceName,[request.ResourceName]);
            if (!request.Package.CanExport) Add("Pacote com erros: " + request.ResourceName, [request.ResourceName]);
            try { SafePaths.ValidateResourceName(request.ResourceName); }
            catch (InvalidDataException e) { Add(request.ResourceName + ": " + e.Message, [request.ResourceName]); }
        }
        foreach (var group in requests.GroupBy(r=>r.ResourceName,StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1))
            Add("Nome de origem repetido: " + group.Key, group.Select(r=>r.ResourceName));
        foreach (var group in requests.SelectMany(r=>r.Package.Vehicles.Select(v=>(v.ModelName,r.ResourceName)))
            .GroupBy(v=>v.ModelName,StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1))
            Add("Modelo repetido: " + group.Key, group.Select(v=>v.ResourceName));
        var valid = requests.Where(r=>r.Package.CanExport).ToArray();
        var packages = valid.Select(r=>r.Package).ToArray();
        foreach (var conflict in await LayoutCompatibility.FindConflictsAsync(packages,ct))
            Add($"Definição de layout conflitante {conflict.Section}/{conflict.Name}", conflict.Owners.Select(i=>valid[i].ResourceName));
        var kitIds = new Dictionary<string,List<string>>(StringComparer.OrdinalIgnoreCase);
        var kitNames = new Dictionary<string,List<string>>(StringComparer.OrdinalIgnoreCase);
        var handling = new Dictionary<string,List<(string Owner,XElement Xml)>>(StringComparer.OrdinalIgnoreCase);
        static void Register(Dictionary<string,List<string>> dictionary,string key,string owner)
        { if (!dictionary.TryGetValue(key,out var owners)) dictionary[key] = owners = []; owners.Add(owner); }
        foreach (var request in valid)
        foreach (var file in request.Package.Files.Where(f=>f.DataFileType is "CARCOLS_FILE" or "HANDLING_FILE"))
        {
            ct.ThrowIfCancellationRequested();
            var xml = await Metadata.ReadAsync(file.Content,ct);
            if (file.DataFileType == "CARCOLS_FILE")
            {
                foreach (var item in xml.Root?.Elements().Where(e=>e.Name.LocalName=="Kits").SelectMany(e=>e.Elements()) ?? [])
                {
                    var id = item.Elements().FirstOrDefault(e=>e.Name.LocalName=="id")?.Attribute("value")?.Value;
                    if (id is not null)
                    {
                        if (uint.TryParse(id,out var number)) Register(kitIds,number.ToString(System.Globalization.CultureInfo.InvariantCulture),request.ResourceName);
                        else Add("ID de modkit inválido em " + request.ResourceName,[request.ResourceName]);
                    }
                    var name = Metadata.Value(item,"kitName");
                    if (name.Length>0) Register(kitNames,name,request.ResourceName);
                }
            }
            else foreach (var item in xml.Root?.Elements().Where(e=>e.Name.LocalName=="HandlingData").SelectMany(e=>e.Elements()) ?? [])
            {
                var name = Metadata.Value(item,"handlingName");if(name.Length==0)continue;
                var copy = new XElement(item);
                foreach(var node in copy.DescendantNodes().OfType<XText>().Where(t=>string.IsNullOrWhiteSpace(t.Value)).ToArray())node.Remove();
                if(!handling.TryGetValue(name,out var entries))handling[name]=entries=[];
                entries.Add((request.ResourceName,copy));
            }
        }
        foreach(var (label,dictionary) in new[]{("ID de modkit",kitIds),("Nome de modkit",kitNames)})
        foreach(var pair in dictionary.Where(p=>p.Value.Distinct(StringComparer.OrdinalIgnoreCase).Count()>1))
            Add(label + " repetido (" + pair.Key + ")",pair.Value);
        foreach(var pair in handling.Where(p=>p.Value.Skip(1).Any(v=>v.Owner!=p.Value[0].Owner && !XNode.DeepEquals(v.Xml,p.Value[0].Xml))))
            Add("Handling conflitante: " + pair.Key,pair.Value.Select(v=>v.Owner));
        foreach(var group in valid.SelectMany(r=>r.Package.Files.Where(f=>f.DataFileType is null).Select(f=>(File:f,Owner:r.ResourceName)))
            .GroupBy(v=>Path.GetFileName(v.File.DestinationPath),StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1))
        {
            byte[]? first=null;var differs=false;
            foreach(var entry in group)
            {
                ct.ThrowIfCancellationRequested();
                await using var stream=await entry.File.Content.OpenReadAsync(ct);
                var hash=await SHA256.HashDataAsync(stream,ct);
                if(first is null)first=hash;else if(!first.AsSpan().SequenceEqual(hash))differs=true;
            }
            if(differs)Add("Asset conflitante: " + group.Key,group.Select(v=>v.Owner));
        }
        return new(issues);
    }
}

public sealed partial class VehicleConverter
{
    public Task<PackValidationResult> ValidatePackAsync(IReadOnlyList<PackageExportRequest> packages,CancellationToken cancellationToken=default) =>
        Task.Run(()=>PackValidator.ValidateAsync(packages,cancellationToken),cancellationToken);
}