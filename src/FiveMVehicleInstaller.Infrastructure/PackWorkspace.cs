using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FiveMVehicleInstaller.Core;

namespace FiveMVehicleInstaller.Infrastructure;

internal sealed record PackFileChange(string Path, bool HadOriginal, string? NewHash);
internal sealed record PackUpdateJournal(string Target, string Revision, bool Committed, IReadOnlyList<PackFileChange> Changes);

internal static class PackWorkspace
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented=true,PropertyNameCaseInsensitive=true };
    private static string Checked(string root,string relative)
    { var path=SafePaths.Within(root,relative);SafePaths.RejectReparsePoints(path);return path; }
    public static async Task<string> FingerprintAsync(string root,CancellationToken ct)
    {
        SafePaths.RejectReparsePoints(root);
        using var fingerprint=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach(var file in EnumerateFiles(root).Order(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();var relative=SafePaths.NormalizeRelative(Path.GetRelativePath(root,file));
            fingerprint.AppendData(Encoding.UTF8.GetBytes(relative.ToLowerInvariant()+"\0"));
            await using var stream=File.OpenRead(Checked(root,relative));
            fingerprint.AppendData(await SHA256.HashDataAsync(stream,ct));
        }
        return Convert.ToHexString(fingerprint.GetHashAndReset());
    }
    private static IEnumerable<string> EnumerateFiles(string root)
    {
        foreach(var item in Directory.EnumerateFileSystemEntries(root))
        {
            SafePaths.RejectReparsePoints(item);
            if(Directory.Exists(item))foreach(var file in EnumerateFiles(item))yield return file;
            else yield return item;
        }
    }
    private static FileStream Lock(string parent,string name)
    {
        var path=Checked(parent,".fv-lock-"+name);
        return new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None,1,FileOptions.DeleteOnClose);
    }
    public static async Task RecoverAsync(string target,CancellationToken ct)
    {
        var parent=Path.GetDirectoryName(target)!;var name=SafePaths.ValidateResourceName(Path.GetFileName(target));
        foreach(var transaction in Directory.EnumerateDirectories(parent,".fv-update-"+name+"-*"))
        {
            ct.ThrowIfCancellationRequested();SafePaths.RejectReparsePoints(transaction);
            var suffix=Path.GetFileName(transaction)[(".fv-update-"+name+"-").Length..];
            if(!Guid.TryParseExact(suffix,"N",out _))continue;
            var journalPath=Checked(transaction,"journal.json");
            if(!File.Exists(journalPath))continue;
            if(new FileInfo(journalPath).Length>16*1024*1024)throw new InvalidDataException("Registro de atualização excede o limite.");
            var journal=JsonSerializer.Deserialize<PackUpdateJournal>(await File.ReadAllTextAsync(journalPath,ct),Json)
                ?? throw new InvalidDataException("Registro de atualização inválido: "+transaction);
            if(!Path.GetFullPath(journal.Target).Equals(target,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Registro de atualização aponta para outro pack.");
            if(!journal.Committed)await RollbackAsync(target,transaction,journal.Changes);
            SafePaths.DeleteOwnedDirectory(parent,transaction);
        }
    }
    private static async Task RollbackAsync(string target,string transaction,IReadOnlyList<PackFileChange> changes)
    {
        foreach(var change in changes.Reverse())
        {
            var path=Checked(target,change.Path);var backup=Checked(transaction,"before/"+change.Path);
            if(File.Exists(backup))
            {
                if(File.Exists(path))
                {
                    if(change.NewHash is null || await HashAsync(path,CancellationToken.None)!=change.NewHash)
                        throw new IOException("Um arquivo mudou durante a recuperação: "+path);
                    File.Delete(path);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.Move(backup,path);
            }
            else if(!change.HadOriginal && File.Exists(path))
            {
                if(change.NewHash is null || await HashAsync(path,CancellationToken.None)!=change.NewHash)
                    throw new IOException("Não foi possível reverter um arquivo modificado externamente: "+path);
                File.Delete(path);
            }
            RemoveEmptyParents(target,Path.GetDirectoryName(path)!);
        }
    }
    private static void RemoveEmptyParents(string root,string path)
    {
        var normalized=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        while(path.StartsWith(normalized,StringComparison.OrdinalIgnoreCase))
        {
            SafePaths.RejectReparsePoints(path);
            if(!Directory.Exists(path)||Directory.EnumerateFileSystemEntries(path).Any())break;
            Directory.Delete(path);path=Path.GetDirectoryName(path)!;
        }
    }
    private static async Task<string> HashAsync(string file,CancellationToken ct)
    { await using var stream=File.OpenRead(file);return Convert.ToHexString(await SHA256.HashDataAsync(stream,ct)); }
    private static async Task SaveJournalAsync(string directory,PackUpdateJournal journal)
    {
        var temporary=Checked(directory,"journal.tmp");
        await File.WriteAllTextAsync(temporary,JsonSerializer.Serialize(journal,Json));
        File.Move(temporary,Checked(directory,"journal.json"),overwrite:true);
    }
    public static async Task<PackUpdateResult> UpdateAsync(PackDocument document,ComposedPack composed,
        IProgress<ConversionProgress>? progress,CancellationToken ct)
    {
        var target=Path.GetFullPath(document.Directory).TrimEnd(Path.DirectorySeparatorChar);
        var name=SafePaths.ValidateResourceName(Path.GetFileName(target));var parent=Path.GetDirectoryName(target)!;
        if(name!=document.ResourceName)throw new InvalidDataException("O nome ou caminho do pack foi alterado.");
        SafePaths.RejectReparsePoints(target);using var exclusive=Lock(parent,name);
        await RecoverAsync(target,ct);
        if(await FingerprintAsync(target,ct)!=document.Revision)throw new IOException("O pack mudou fora do programa. Abra-o novamente antes de salvar.");
        var indexPath=Checked(target,PackOrganizer.IndexName);
        if(new FileInfo(indexPath).Length>16*1024*1024)throw new InvalidDataException("Índice muito grande.");
        var old=JsonSerializer.Deserialize<PackIndex>(await File.ReadAllTextAsync(indexPath,ct),Json)??throw new InvalidDataException("Índice inválido.");
        var managed=old.Packages.SelectMany(p=>p.Files).Select(f=>SafePaths.NormalizeRelative(f.PackPath))
            .Concat([PackOrganizer.IndexName,"fxmanifest.lua","relatorio.txt"]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var desired=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var transaction=Checked(parent,".fv-update-"+name+"-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(transaction);var changes=new List<PackFileChange>();var retain=false;
        try
        {
            async Task Stage(string relative,Func<Stream,Task> write,IContentSource? content=null)
            {
                ct.ThrowIfCancellationRequested();relative=SafePaths.NormalizeRelative(relative);
                if(!desired.Add(relative))throw new InvalidDataException("Caminho repetido: "+relative);
                var destination=Checked(target,relative);
                if(Directory.Exists(destination))throw new IOException("Uma pasta ocupa o destino: "+relative);
                if(File.Exists(destination)&&!managed.Contains(relative))throw new IOException("Arquivo externo ocupa o destino: "+relative);
                // Reused assets retain their bytes and timestamps without being rewritten.
                if(content is FileContentSource local && local.FilePath.Equals(destination,StringComparison.OrdinalIgnoreCase))return;
                if(content is not null&&File.Exists(destination)&&new FileInfo(destination).Length==content.Length)
                {
                    await using var input=await content.OpenReadAsync(ct);
                    var expected=Convert.ToHexString(await SHA256.HashDataAsync(input,ct));
                    if(expected==await HashAsync(destination,ct))return;
                }
                var staged=Checked(transaction,"after/"+relative);Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
                await using(var output=File.Create(staged))await write(output);
                var hash=await HashAsync(staged,ct);
                if(File.Exists(destination)&&hash==await HashAsync(destination,ct)){File.Delete(staged);return;}
                changes.Add(new(relative,File.Exists(destination),hash));
            }
            var package=composed.Package;var done=0;
            foreach(var file in package.Files)
            {
                progress?.Report(new("Preparando atualização: "+file.DestinationPath,done++,package.Files.Count));
                var edited=file.DataFileType=="VEHICLE_METADATA_FILE"&&package.Vehicles.Any(v=>v.MetadataPath==file.SourcePath&&
                    composed.AudioOverrides.TryGetValue(v.Key,out var value)&&value.Trim()!=v.AudioNameHash);
                await Stage(file.DestinationPath,async output=>
                {
                    if(file.DataFileType=="VEHICLE_METADATA_FILE")await Metadata.WriteAudioAsync(file,package,composed.AudioOverrides,output,ct);
                    else {await using var input=await file.Content.OpenReadAsync(ct);await input.CopyToAsync(output,ct);}
                },edited?null:file.Content);
            }
            await Stage("fxmanifest.lua",s=>s.WriteAsync(Encoding.UTF8.GetBytes(VehicleConverter.CreateManifest(package)),ct).AsTask());
            var report=new StringBuilder().AppendLine("Pack atualizado: "+name).AppendLine("ensure "+name);
            ModAttribution.AppendReport(report,package);
            foreach(var diagnostic in package.Diagnostics)report.AppendLine($"[{diagnostic.Severity}] {diagnostic.Message}");
            foreach(var vehicle in package.Vehicles)report.AppendLine(vehicle.ModelName+": audioNameHash = "+composed.AudioOverrides.GetValueOrDefault(vehicle.Key,vehicle.AudioNameHash));
            await Stage("relatorio.txt",s=>s.WriteAsync(Encoding.UTF8.GetBytes(report.ToString()),ct).AsTask());
            foreach(var path in managed.Except(desired,StringComparer.OrdinalIgnoreCase))
                if(File.Exists(Checked(target,path)))changes.Add(new(path,true,null));
            if(await FingerprintAsync(target,ct)!=document.Revision)throw new IOException("O pack mudou durante a preparação. Abra-o novamente.");
            var journal=new PackUpdateJournal(target,document.Revision,false,changes);
            await SaveJournalAsync(transaction,journal);
            try
            {
                for(var i=0;i<changes.Count;i++)
                {
                    ct.ThrowIfCancellationRequested();var change=changes[i];
                    progress?.Report(new("Aplicando atualização: "+change.Path,i,changes.Count));
                    ct.ThrowIfCancellationRequested();var path=Checked(target,change.Path);
                    if(change.HadOriginal)
                    {
                        var backup=Checked(transaction,"before/"+change.Path);Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                        File.Move(path,backup);
                    }
                    if(change.NewHash is not null)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);SafePaths.RejectReparsePoints(path);
                        File.Move(Checked(transaction,"after/"+change.Path),path);
                    }
                }
                ct.ThrowIfCancellationRequested();await SaveJournalAsync(transaction,journal with{Committed=true});
            }
            catch
            {
                try{await RollbackAsync(target,transaction,changes);}
                catch(Exception rollback){retain=true;throw new IOException("A recuperação precisa ser concluída ao reabrir o pack. Registro: "+transaction,rollback);}
                throw;
            }
            foreach(var change in changes.Where(c=>c.NewHash is null))RemoveEmptyParents(target,Path.GetDirectoryName(Checked(target,change.Path))!);
            var revision=await FingerprintAsync(target,CancellationToken.None);
            progress?.Report(new("Pack atualizado: "+changes.Count+" arquivo(s) alterado(s).",changes.Count,changes.Count));
            return new(new(target,name,revision),new(target,"ensure "+name,package.Files.Count));
        }
        finally
        {
            if(!retain&&Directory.Exists(transaction))
            {
                try{SafePaths.DeleteOwnedDirectory(parent,transaction);}catch(IOException){ }catch(UnauthorizedAccessException){ }
            }
        }
    }
    public static async Task RecoverBeforeOpenAsync(string directory,CancellationToken ct)
    {
        var root=Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        var parent=Path.GetDirectoryName(root)??throw new InvalidDataException("Selecione a pasta do resource.");
        using var exclusive=Lock(parent,SafePaths.ValidateResourceName(Path.GetFileName(root)));
        await RecoverAsync(root,ct);
    }
}

public sealed partial class VehicleConverter
{
    public async Task<OpenPackResult> OpenPackAsync(string directory,IProgress<ConversionProgress>? progress=null,CancellationToken cancellationToken=default)
    {
        var root=Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        await PackWorkspace.RecoverBeforeOpenAsync(root,cancellationToken);
        if(!File.Exists(SafePaths.Within(root,PackOrganizer.IndexName)))throw new InvalidDataException("Esta pasta não tem o índice .fvpack.json do aplicativo. Resources FiveM externos não são importados.");
        var session=await AnalyzeAsync([root],progress,cancellationToken);
        try
        {
            if(session.Packages.Count==0||session.Diagnostics.Any(d=>d.Severity==DiagnosticSeverity.Error)||session.Packages.Any(p=>!p.CanExport))
                throw new InvalidDataException("Não foi possível abrir o pack: "+string.Join("; ",session.Diagnostics.Concat(session.Packages.SelectMany(p=>p.Diagnostics)).Where(d=>d.Severity==DiagnosticSeverity.Error).Select(d=>d.Message)));
            return new(new(root,SafePaths.ValidateResourceName(Path.GetFileName(root)),await PackWorkspace.FingerprintAsync(root,cancellationToken)),session);
        }
        catch{session.Dispose();throw;}
    }
    public async Task<PackUpdateResult> UpdatePackAsync(PackDocument document,IReadOnlyList<PackageExportRequest> packages,
        IProgress<ConversionProgress>? progress=null,CancellationToken cancellationToken=default)
    {
        var validation=await ValidatePackAsync(packages,cancellationToken);
        if(!validation.CanExport)throw new InvalidDataException(string.Join("\n",validation.Issues.Select(i=>i.Message+": "+string.Join(", ",i.Packages))));
        return await Task.Run(async()=>
        {
            var composed=await PackOrganizer.ComposeAsync(packages,progress,cancellationToken);
            return await PackWorkspace.UpdateAsync(document,composed,progress,cancellationToken);
        },cancellationToken);
    }
}
