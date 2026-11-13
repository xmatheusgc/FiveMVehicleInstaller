using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FiveMVehicleInstaller.Core;
using FiveMVehicleInstaller.Infrastructure;

namespace FiveMVehicleInstaller.Tests;

public sealed class PackWorkspaceTests
{
    private static PackageExportRequest Request(VehiclePackage package,string? sound=null) => new(package,package.SuggestedResourceName,
        sound is null ? new Dictionary<string,string>() : package.Vehicles.ToDictionary(v=>v.Key,_=>sound));
    private static Dictionary<string,string> Snapshot(string root) => Directory.GetFiles(root,"*",SearchOption.AllDirectories)
        .ToDictionary(p=>Path.GetRelativePath(root,p),p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
    private static void SameSnapshot(Dictionary<string,string> expected,string root)
    {var actual=Snapshot(root);Assert.Equal(expected.Count,actual.Count);foreach(var pair in expected)Assert.Equal(pair.Value,actual[pair.Key]);}
    private static void NoTransaction(TestWorkspace w) => Assert.Empty(Directory.GetDirectories(w.PathFor("packs"),".fv-update-*"));

    [Fact]
    public async Task UpdatesMembershipAndSoundWithoutOriginalRpfsAndDoesNotRewriteUnchangedAssets()
    {
        using var w=new TestWorkspace();var converter=new VehicleConverter(temporaryParent:w.PathFor("temp"));
        var a=PackageFixture.WriteFolder(w,"a");var b=PackageFixture.WriteFolder(w,"b","other");var c=PackageFixture.WriteFolder(w,"c","third");
        w.Write("a/shared.ytd",RpfFixture.Resource([6]));w.Write("b/shared.ytd",RpfFixture.Resource([6]));
        using(var inputs=await converter.AnalyzeAsync([a,b]))
            await converter.ExportPackAsync(inputs.Packages.Select(p=>Request(p)).ToArray(),new("garage",w.PathFor("packs")));
        // The pack is self-contained; its original import folders can disappear.
        SafePaths.RejectReparsePoints(a);Directory.Move(a,w.PathFor("old_a"));Directory.Move(b,w.PathFor("old_b"));
        var opened=await converter.OpenPackAsync(w.PathFor("packs/garage"));using var originalSession=opened.Session;
        var retained=Assert.Single(opened.Session.Packages,p=>p.Vehicles[0].ModelName=="other");
        var stable=w.PathFor("packs/garage/stream/other/other.yft");var timestamp=File.GetLastWriteTimeUtc(stable);
        w.WriteText("packs/garage/personal-notes.txt","preserve me");
        // Reopen after an external change, as a real editing session must do.
        var refreshed=await converter.OpenPackAsync(w.PathFor("packs/garage"));using var refreshedSession=refreshed.Session;
        retained=Assert.Single(refreshedSession.Packages,p=>p.Vehicles[0].ModelName=="other");
        using var added=await converter.AnalyzeAsync([c]);
        await converter.UpdatePackAsync(refreshed.Document,[Request(retained,"ADDER"),Request(Assert.Single(added.Packages))]);
        Assert.Equal(timestamp,File.GetLastWriteTimeUtc(stable));Assert.False(File.Exists(w.PathFor("packs/garage/stream/car/car.yft")));
        Assert.True(File.Exists(w.PathFor("packs/garage/stream/other/shared.ytd")));Assert.Equal("preserve me",File.ReadAllText(w.PathFor("packs/garage/personal-notes.txt")));
        var after=await converter.OpenPackAsync(w.PathFor("packs/garage"));using var afterSession=after.Session;
        Assert.Equal(new[]{"other","third"},afterSession.Packages.SelectMany(p=>p.Vehicles).Select(v=>v.ModelName).Order().ToArray());
        var other=Assert.Single(afterSession.Packages,p=>p.Vehicles[0].ModelName=="other");Assert.Equal("ADDER",other.Vehicles[0].AudioNameHash);
        Assert.Equal(PackageFixture.Vehicles(("other","SULTAN")).Replace(">SULTAN<",">ADDER<"),File.ReadAllText(w.PathFor("packs/garage/data/other/vehicles.meta")));
        await converter.UpdatePackAsync(after.Document,afterSession.Packages.Select(p=>Request(p)).ToArray());
        NoTransaction(w);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CancellationBeforeOrDuringCommitRestoresExactPack(bool commit)
    {
        using var w=new TestWorkspace();var converter=new VehicleConverter(temporaryParent:w.PathFor("temp"));
        using var inputs=await converter.AnalyzeAsync([PackageFixture.WriteFolder(w,"input")]);
        await converter.ExportPackAsync(inputs.Packages.Select(p=>Request(p)).ToArray(),new("garage",w.PathFor("packs")));
        var opened=await converter.OpenPackAsync(w.PathFor("packs/garage"));using var session=opened.Session;var before=Snapshot(opened.Document.Directory);
        using var added=await converter.AnalyzeAsync([PackageFixture.WriteFolder(w,"added","third")]);using var cancellation=new CancellationTokenSource();
        var progress=new ImmediateProgress(p=>
        {
            if(commit?p.Message.StartsWith("Aplicando atualização:")&&p.Completed>0:p.Message.StartsWith("Preparando atualização:")&&p.Completed>0)cancellation.Cancel();
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>converter.UpdatePackAsync(opened.Document,
            [Request(Assert.Single(session.Packages),"ADDER"),Request(Assert.Single(added.Packages))],progress,cancellation.Token));
        SameSnapshot(before,opened.Document.Directory);NoTransaction(w);
    }

    [Fact]
    public async Task LockedFileDuringCommitRollsBackFilesAlreadyChanged()
    {
        using var w=new TestWorkspace();var converter=new VehicleConverter(temporaryParent:w.PathFor("temp"));
        using var inputs=await converter.AnalyzeAsync([PackageFixture.WriteFolder(w,"input")]);
        await converter.ExportPackAsync(inputs.Packages.Select(p=>Request(p)).ToArray(),new("garage",w.PathFor("packs")));
        var opened=await converter.OpenPackAsync(w.PathFor("packs/garage"));using var session=opened.Session;var before=Snapshot(opened.Document.Directory);
        using var added=await converter.AnalyzeAsync([PackageFixture.WriteFolder(w,"added","third")]);
        using(var locked=new FileStream(Path.Combine(opened.Document.Directory,"fxmanifest.lua"),FileMode.Open,FileAccess.Read,FileShare.Read))
            await Assert.ThrowsAsync<IOException>(()=>converter.UpdatePackAsync(opened.Document,[Request(Assert.Single(session.Packages),"ADDER"),Request(Assert.Single(added.Packages))]));
        SameSnapshot(before,opened.Document.Directory);NoTransaction(w);
    }

    [Fact]
    public async Task ExternalModificationBlocksUpdateWithoutOverwritingAnyFile()
    {
        using var w=new TestWorkspace();var converter=new VehicleConverter(temporaryParent:w.PathFor("temp"));
        using var inputs=await converter.AnalyzeAsync([PackageFixture.WriteFolder(w,"input")]);
        await converter.ExportPackAsync(inputs.Packages.Select(p=>Request(p)).ToArray(),new("garage",w.PathFor("packs")));
        var opened=await converter.OpenPackAsync(w.PathFor("packs/garage"));using var session=opened.Session;
        w.WriteText("packs/garage/relatorio.txt","external change");var before=Snapshot(opened.Document.Directory);
        var error=await Assert.ThrowsAsync<IOException>(()=>converter.UpdatePackAsync(opened.Document,session.Packages.Select(p=>Request(p)).ToArray()));
        Assert.Contains("mudou fora",error.Message);SameSnapshot(before,opened.Document.Directory);NoTransaction(w);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task RecoversInterruptedCommitAndRejectsTraversalJournal(bool malicious)
    {
        using var w=new TestWorkspace();var converter=new VehicleConverter(temporaryParent:w.PathFor("temp"));
        using var inputs=await converter.AnalyzeAsync([PackageFixture.WriteFolder(w,"input")]);
        var exported=await converter.ExportPackAsync(inputs.Packages.Select(p=>Request(p)).ToArray(),new("garage",w.PathFor("packs")));
        var before=Snapshot(exported.ResourceDirectory);var transaction="packs/.fv-update-garage-"+Guid.NewGuid().ToString("N");
        var path="data/car/vehicles.meta";var source=SafePaths.Within(exported.ResourceDirectory,path);
        var old=File.ReadAllBytes(source);w.Write(transaction+"/before/"+path,old);w.Write("packs/garage/"+path,Encoding.UTF8.GetBytes("interrupted"));
        var journal=new {Target=exported.ResourceDirectory,Revision="",Committed=false,Changes=new[]{new {Path=malicious?"../escape.meta":path,HadOriginal=true,NewHash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("interrupted")))}}};
        w.WriteText(transaction+"/journal.json",JsonSerializer.Serialize(journal));
        if(malicious)
        {
            await Assert.ThrowsAsync<InvalidDataException>(()=>converter.OpenPackAsync(exported.ResourceDirectory));
            Assert.False(File.Exists(w.PathFor("packs/escape.meta")));Assert.True(Directory.Exists(w.PathFor(transaction)));
        }
        else
        {
            var opened=await converter.OpenPackAsync(exported.ResourceDirectory);using var session=opened.Session;
            Assert.Single(session.Packages);SameSnapshot(before,exported.ResourceDirectory);NoTransaction(w);
        }
    }

    [Fact]
    public async Task PreflightIsolatesLayoutsAndStillReportsAssetAndModkitConflictsWithoutOutput()
    {
        using var w=new TestWorkspace();var a=PackageFixture.WriteFolder(w,"a");var b=PackageFixture.WriteFolder(w,"b","other");
        w.Write("a/shared.yft",RpfFixture.Resource([1]));w.Write("b/shared.yft",RpfFixture.Resource([2]));
        foreach(var folder in new[]{"a","b"})w.WriteText(folder+"/data/carcols.meta","<CVehicleModelInfoVarGlobal><Kits><Item><id value='3190'/></Item></Kits></CVehicleModelInfoVarGlobal>");
        w.WriteText("a/data/vehiclelayouts.meta","<CVehicleMetadataMgr><VehicleSeatInfos><Item><Name>RHD</Name><Bone>left</Bone></Item></VehicleSeatInfos></CVehicleMetadataMgr>");
        w.WriteText("b/data/vehiclelayouts.meta","<CVehicleMetadataMgr><VehicleSeatInfos><Item><Name>RHD</Name><Bone>right</Bone></Item></VehicleSeatInfos></CVehicleMetadataMgr>");
        var converter=new VehicleConverter(temporaryParent:w.PathFor("temp"));using var session=await converter.AnalyzeAsync([a,b]);
        var result=await converter.ValidatePackAsync(session.Packages.Select(p=>Request(p)).ToArray());
        Assert.False(result.CanExport);Assert.Equal(2,result.Issues.Count);
        Assert.Contains(result.Issues,i=>i.Message.Contains("shared.yft"));Assert.Contains(result.Issues,i=>i.Message.Contains("3190"));Assert.DoesNotContain(result.Issues,i=>i.Message.Contains("RHD"));
        Assert.All(result.Issues,i=>Assert.Equal(2,i.Packages.Count));Assert.False(Directory.Exists(w.PathFor("out")));
    }
    private sealed class ImmediateProgress(Action<ConversionProgress> callback):IProgress<ConversionProgress>{public void Report(ConversionProgress value)=>callback(value);}
}