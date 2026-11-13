using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FiveMVehicleInstaller.Core;
using FiveMVehicleInstaller.Infrastructure;

namespace FiveMVehicleInstaller.Tests;

public sealed class PackOrganizerTests
{
    [Fact]
    public async Task DiscoversIndependentResourcesAndRespectsDeclaredMetadata()
    {
        using var w = new TestWorkspace(); var converter = new VehicleConverter(temporaryParent: w.PathFor("temp"));
        foreach (var model in new[] { "car", "other" })
        {
            var input = PackageFixture.WriteFolder(w, "input/" + model, model);
            using var analysis = await converter.AnalyzeAsync([input]);
            await converter.ExportAsync(Assert.Single(analysis.Packages), new(model, w.PathFor("resources"), new Dictionary<string, string>()));
        }
        w.WriteText("resources/car/notes.txt", "<CHandlingDataMgr><HandlingData><alternative/></HandlingData></CHandlingDataMgr>");
        using var resources = await converter.AnalyzeAsync([w.PathFor("resources")]);
        Assert.Equal(2, resources.Packages.Count);
        Assert.All(resources.Packages, p => Assert.True(p.CanExport, string.Join("; ", p.Diagnostics.Select(d => d.Message))));
        Assert.DoesNotContain(resources.Packages.SelectMany(p => p.Files), f => f.SourcePath.Contains("alternative"));
    }

    [Fact]
    public async Task JoinsAndSeparatesPreservingBytesSoundsAndSharedAssetsWithOneFolderPerOrigin()
    {
        using var w = new TestWorkspace(); var a = PackageFixture.WriteFolder(w, "a"); var b = PackageFixture.WriteFolder(w, "b", "other");
        w.Write("a/stream/shared/interior.ytd", RpfFixture.Resource([4]));
        w.Write("b/stream/shared/interior.ytd", RpfFixture.Resource([4]));
        w.Write("a/x64/vehiclemods/car_mods.rpf", RpfFixture.Create(new FixtureFile("body/car_bumper.yft", RpfFixture.Resource([5]), Resource: true)));
        var converter = new VehicleConverter(temporaryParent: w.PathFor("temp"));
        using var analyzed = await converter.AnalyzeAsync([a,b]);
        Assert.Equal(2, analyzed.Packages.Count);
        var requests = analyzed.Packages.Select((p,i) => new PackageExportRequest(p, "origin" + i,
            i == 0 ? new Dictionary<string,string> { [p.Vehicles[0].Key] = "CUSTOM_ENGINE" } : [])).ToArray();
        var packed = await converter.ExportPackAsync(requests, new("collection", w.PathFor("packs")));
        Assert.True(File.Exists(Path.Combine(packed.ResourceDirectory, ".fvpack.json")));
        Assert.Single(Directory.GetFiles(packed.ResourceDirectory, "interior.ytd", SearchOption.AllDirectories));
        Assert.True(File.Exists(Path.Combine(packed.ResourceDirectory, "stream/origin0/car_bumper.yft")));
        Assert.Empty(Directory.GetDirectories(Path.Combine(packed.ResourceDirectory, "stream/origin0")));
        Assert.Equal(Encoding.UTF8.GetBytes(PackageFixture.Vehicles(("car", "SULTAN")).Replace(">SULTAN<", ">CUSTOM_ENGINE<")),
            await File.ReadAllBytesAsync(Path.Combine(packed.ResourceDirectory, "data/origin0/vehicles.meta")));
        using var recovered = await converter.AnalyzeAsync([packed.ResourceDirectory]);
        Assert.Equal(2, recovered.Packages.Count);
        Assert.All(recovered.Packages, p => Assert.True(p.CanExport, string.Join(";", p.Diagnostics.Select(d => d.Message))));
        foreach (var (package, i) in recovered.Packages.Select((p,i) => (p,i)))
        {
            Assert.Equal("origin" + i, package.SuggestedResourceName);
            var result = await converter.ExportAsync(package, new(package.SuggestedResourceName, w.PathFor("separate/" + i), new Dictionary<string,string>()));
            foreach (var original in analyzed.Packages[i].Files)
            {
                await using var originalStream = await original.Content.OpenReadAsync(default);
                using var bytes = new MemoryStream(); await originalStream.CopyToAsync(bytes);
                var expected = bytes.ToArray();
                if (i == 0 && original.DataFileType == "VEHICLE_METADATA_FILE")
                    expected = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(expected).Replace(">SULTAN<", ">CUSTOM_ENGINE<"));
                Assert.Equal(expected, await File.ReadAllBytesAsync(SafePaths.Within(result.ResourceDirectory, original.DestinationPath)));
            }
        }
        Assert.Equal(PackageFixture.Vehicles(("car", "SULTAN")), await File.ReadAllTextAsync(Path.Combine(a,"data/vehicles.meta")));
        var repacked = await converter.ExportPackAsync(recovered.Packages.Select(p => new PackageExportRequest(p,p.SuggestedResourceName, new Dictionary<string,string>())).ToArray(), new("again", w.PathFor("packs")));
        foreach (var path in Directory.GetFiles(packed.ResourceDirectory,"*",SearchOption.AllDirectories).Where(p => !p.EndsWith("relatorio.txt")))
        {
            var otherPath = Path.Combine(repacked.ResourceDirectory, Path.GetRelativePath(packed.ResourceDirectory,path));
            var expected = SHA256.HashData(await File.ReadAllBytesAsync(path));
            var actual = SHA256.HashData(await File.ReadAllBytesAsync(otherPath));
            Assert.Equal(expected,actual);
        }
    }

    [Fact]
    public async Task ImportsEarlierPackIndexesAndSimplifiesLegacyStreamPathsOnExport()
    {
        using var w = new TestWorkspace(); var input = PackageFixture.WriteFolder(w,"input");
        var converter = new VehicleConverter(temporaryParent:w.PathFor("temp")); using var session=await converter.AnalyzeAsync([input]);
        var pack=await converter.ExportPackAsync([new(Assert.Single(session.Packages),"origin",new Dictionary<string,string>())],new("legacy",w.PathFor("packs")));
        var indexPath=Path.Combine(pack.ResourceDirectory,".fvpack.json");
        var index=JsonNode.Parse(await File.ReadAllTextAsync(indexPath))!;
        var mapping=index["Packages"]![0]!["Files"]!.AsArray().Single(f=>f!["PackPath"]!.GetValue<string>().EndsWith("car.yft"))!;
        var previous=SafePaths.Within(pack.ResourceDirectory,mapping["PackPath"]!.GetValue<string>());
        var legacyPath="stream/origin/x64/levels/vehicles/car.yft";
        Directory.CreateDirectory(Path.GetDirectoryName(SafePaths.Within(pack.ResourceDirectory,legacyPath))!);
        File.Move(previous,SafePaths.Within(pack.ResourceDirectory,legacyPath));
        mapping["PackPath"]=legacyPath; mapping["OriginalPath"]="stream/x64/levels/vehicles/car.yft";
        await File.WriteAllTextAsync(indexPath,index.ToJsonString());
        using var restored=await converter.AnalyzeAsync([pack.ResourceDirectory]); var package=Assert.Single(restored.Packages);
        Assert.True(package.CanExport,string.Join(";",package.Diagnostics.Select(d=>d.Message)));
        var separate=await converter.ExportAsync(package,new("origin",w.PathFor("separate"),new Dictionary<string,string>()));
        Assert.Empty(Directory.GetDirectories(Path.Combine(separate.ResourceDirectory,"stream")));
        Assert.Equal(RpfFixture.Resource([1,2,3,4]),await File.ReadAllBytesAsync(Path.Combine(separate.ResourceDirectory,"stream/car.yft")));
        var repacked=await converter.ExportPackAsync([new(package,"origin",new Dictionary<string,string>())],new("new",w.PathFor("packs")));
        Assert.Empty(Directory.GetDirectories(Path.Combine(repacked.ResourceDirectory,"stream/origin")));
    }
    [Theory]
    [InlineData("model")][InlineData("asset")][InlineData("kit-id")][InlineData("kit-name")][InlineData("handling")][InlineData("resource-name")][InlineData("unknown-sound")]
    public async Task RejectsConflictsBeforeCreatingAnyOutput(string kind)
    {
        using var w = new TestWorkspace(); var a = PackageFixture.WriteFolder(w,"a"); var b = PackageFixture.WriteFolder(w,"b","other");
        switch(kind)
        {
            case "model": w.WriteText("b/data/vehicles.meta", PackageFixture.Vehicles(("car","SULTAN"))); w.Write("b/car.yft", RpfFixture.Resource([3])); break;
            case "asset": w.Write("a/shared.ytd",RpfFixture.Resource([1])); w.Write("b/shared.ytd",RpfFixture.Resource([2])); break;
            case "kit-id": case "kit-name":
                w.WriteText("a/data/carcols.meta",Kit(20,"first"));
                w.WriteText("b/data/carcols.meta",Kit(kind=="kit-id"?20:21,kind=="kit-name"?"first":"second")); break;
            case "handling":
                w.WriteText("a/data/handling.meta",Handling(1)); w.WriteText("b/data/handling.meta",Handling(2)); break;
        }
        var converter = new VehicleConverter(temporaryParent:w.PathFor("temp"));
        using var session = await converter.AnalyzeAsync([a,b]); Assert.Equal(2,session.Packages.Count);
        Assert.All(session.Packages,p=>Assert.True(p.CanExport));
        var requests = session.Packages.Select((p,i)=>new PackageExportRequest(p,kind=="resource-name"?"same":"origin"+i,
            kind=="unknown-sound"?new Dictionary<string,string>{{"unknown","SULTAN"}}:[])).ToArray();
        await Assert.ThrowsAsync<InvalidDataException>(()=>converter.ExportPackAsync(requests,new("pack",w.PathFor("output"))));
        Assert.False(Directory.Exists(w.PathFor("output")));
    }

    [Theory]
    [InlineData("client_script 'engine.lua'")]
    [InlineData("dependency 'audio_resource'")]
    [InlineData("data_file 'AUDIO_GAMEDATA' 'engine.dat'")]
    [InlineData("local dir = 'data/'")]
    [InlineData("files { getMetadataPath() }")]
    public async Task DoesNotSilentlyDropScriptsAudioDependenciesOrExecuteLua(string extra)
    {
        using var w = new TestWorkspace(); var converter = new VehicleConverter(temporaryParent:w.PathFor("temp"));
        var input=PackageFixture.WriteFolder(w,"input"); using var dlc=await converter.AnalyzeAsync([input]);
        var resource=await converter.ExportAsync(Assert.Single(dlc.Packages),new("car",w.PathFor("resources"),new Dictionary<string,string>()));
        await File.AppendAllTextAsync(Path.Combine(resource.ResourceDirectory,"fxmanifest.lua"),"\n"+extra);
        using var analyzed=await converter.AnalyzeAsync([resource.ResourceDirectory]);
        Assert.Empty(analyzed.Packages);
        Assert.Contains(analyzed.Diagnostics,d=>d.Severity==DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("version")][InlineData("traversal-original")][InlineData("traversal-packed")][InlineData("duplicate-group")][InlineData("missing")][InlineData("unmapped")][InlineData("script")]
    public async Task RejectsUnsafeOrIncompletePackIndexWithoutAffectingIndependentInputs(string kind)
    {
        using var w=new TestWorkspace(); var converter=new VehicleConverter(temporaryParent:w.PathFor("temp"));
        var input=PackageFixture.WriteFolder(w,"input"); using var dlc=await converter.AnalyzeAsync([input]);
        var package=Assert.Single(dlc.Packages);
        var packed=await converter.ExportPackAsync([new(package,"car",new Dictionary<string,string>())],new("pack",w.PathFor("out")));
        var indexPath=Path.Combine(packed.ResourceDirectory,".fvpack.json");
        var index=JsonNode.Parse(await File.ReadAllTextAsync(indexPath))!;
        var groups=index["Packages"]!.AsArray(); var file=groups[0]!["Files"]![0]!;
        switch(kind)
        {
            case "version": index["Version"]=99; break;
            case "traversal-original": file["OriginalPath"]="../escape.meta"; break;
            case "traversal-packed": file["PackPath"]="../escape.meta"; break;
            case "duplicate-group": groups.Add(groups[0]!.DeepClone()); break;
            case "missing": File.Delete(SafePaths.Within(packed.ResourceDirectory,file["PackPath"]!.GetValue<string>())); break;
            case "unmapped": w.Write("out/pack/stream/new.ytd",RpfFixture.Resource([7])); break;
            case "script": await File.AppendAllTextAsync(Path.Combine(packed.ResourceDirectory,"fxmanifest.lua"),"\nclient_script 'main.lua'"); break;
        }
        await File.WriteAllTextAsync(indexPath,index.ToJsonString());
        var valid=PackageFixture.WriteFolder(w,"valid","independent");
        using var analyzed=await converter.AnalyzeAsync([packed.ResourceDirectory,valid]);
        Assert.Equal("independent",Assert.Single(Assert.Single(analyzed.Packages).Vehicles).ModelName);
        Assert.Contains(analyzed.Diagnostics,d=>d.Severity==DiagnosticSeverity.Error);
        Assert.False(File.Exists(w.PathFor("escape.meta")));
    }

    [Fact]
    public async Task PackCancellationAndExistingDestinationNeverLeavePartialOrOverwrittenResource()
    {
        using var w=new TestWorkspace(); var input=PackageFixture.WriteFolder(w,"input");
        var converter=new VehicleConverter(temporaryParent:w.PathFor("temp")); using var session=await converter.AnalyzeAsync([input]);
        var requests=new[]{new PackageExportRequest(Assert.Single(session.Packages),"car",new Dictionary<string,string>())};
        using var cancellation=new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>converter.ExportPackAsync(requests,new("cancelled",w.PathFor("out")),
            new ImmediateProgress(p=> { if(p.Completed>0)cancellation.Cancel(); }),cancellation.Token));
        Assert.Empty(Directory.GetFileSystemEntries(w.PathFor("out")));
        w.WriteText("out/exists/sentinel.txt","keep");
        await Assert.ThrowsAsync<IOException>(()=>converter.ExportPackAsync(requests,new("exists",w.PathFor("out"))));
        Assert.Equal("keep",await File.ReadAllTextAsync(w.PathFor("out/exists/sentinel.txt")));
        Assert.Single(Directory.GetFiles(w.PathFor("out/exists")));
    }

    [Fact]
    public async Task IgnoresUnindexedExternalPackInsteadOfGuessingItsMetadata()
    {
        using var w=new TestWorkspace();
        w.WriteText("external/fxmanifest.lua","fx_version 'cerulean'\ngame 'gta5'\nfiles { 'data/*.meta' }\ndata_file 'VEHICLE_METADATA_FILE' 'data/vehicles.meta'");
        w.WriteText("external/data/vehicles.meta",PackageFixture.Vehicles(("car","SULTAN"),("other","ADDER")));
        w.Write("external/stream/car.yft",RpfFixture.Resource([1]));w.Write("external/stream/other.yft",RpfFixture.Resource([2]));
        var converter=new VehicleConverter(temporaryParent:w.PathFor("temp"));
        using var result=await converter.AnalyzeAsync([w.PathFor("external")]);Assert.Empty(result.Packages);
        await Assert.ThrowsAsync<InvalidDataException>(()=>converter.OpenPackAsync(w.PathFor("external")));
    }
    private static string Kit(int id,string name)=>$"<CVehicleModelInfoVarGlobal><Kits><Item><kitName>{name}</kitName><id value='{id}'/></Item></Kits></CVehicleModelInfoVarGlobal>";
    private static string Handling(int value)=>$"<CHandlingDataMgr><HandlingData><Item><handlingName>SHARED</handlingName><fMass value='{value}'/></Item></HandlingData></CHandlingDataMgr>";
    private sealed class ImmediateProgress(Action<ConversionProgress> callback):IProgress<ConversionProgress>{public void Report(ConversionProgress value)=>callback(value);}
}
