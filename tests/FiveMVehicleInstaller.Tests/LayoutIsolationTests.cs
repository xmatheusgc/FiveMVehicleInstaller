using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using FiveMVehicleInstaller.Core;
using FiveMVehicleInstaller.Infrastructure;

namespace FiveMVehicleInstaller.Tests;

public sealed class LayoutIsolationTests
{
    [Theory]
    [InlineData("utf8", false)]
    [InlineData("utf8", true)]
    [InlineData("utf16le", true)]
    [InlineData("utf16be", false)]
    [InlineData("utf32le", true)]
    [InlineData("utf32be", false)]
    [InlineData("latin1", false)]
    public async Task ChangesOnlyLocalDefinitionsAndReferencesPreservingEncodingAndAudioEdits(string format, bool bom)
    {
        using var w = new TestWorkspace();
        var input = PackageFixture.WriteFolder(w, "input");
        Encoding encoding = format switch
        {
            "utf16le" => new UnicodeEncoding(false, bom), "utf16be" => new UnicodeEncoding(true, bom),
            "utf32le" => new UTF32Encoding(false, bom), "utf32be" => new UTF32Encoding(true, bom),
            "latin1" => Encoding.Latin1, _ => new UTF8Encoding(bom)
        };
        var layout = "<?xml version='1.0' encoding='" + encoding.WebName + "'?>\r\n" +
            "<!-- ação: LOCAL_SEAT car untouched -->\r\n<CVehicleMetadataMgr>\r\n" +
            "<VehicleSeatInfos><Item><Name> LOCAL_SEAT </Name><SeatBoneName>car</SeatBoneName>" +
            "<ShuffleLink>LOCAL_SEAT</ShuffleLink><RearSeatLink>SEAT_STANDARD_REAR_LEFT</RearSeatLink></Item></VehicleSeatInfos>\r\n" +
            "<VehicleSeatAnimInfos><Item><Name>LOCAL_ANIM</Name><InsideClipSetMap ref = 'INSIDE_NATIVE'/></Item></VehicleSeatAnimInfos>\r\n" +
            "<VehicleLayoutInfos><Item><Name>car</Name><Seats><Item><SeatInfo\r\n ref = 'LOCAL_SEAT' />" +
            "<SeatAnimInfo ref=\"LOCAL_ANIM\" /></Item><Item><SeatInfo ref='SEAT_STANDARD_REAR_LEFT'/></Item></Seats></Item></VehicleLayoutInfos>\r\n" +
            "<VehicleCoverBoundOffsetInfos><Item><Name>LOCAL_COVER</Name></Item></VehicleCoverBoundOffsetInfos>" +
            "<VehicleExplosionInfos><Item><Name>LOCAL_EXPLOSION</Name></Item></VehicleExplosionInfos>" +
            "<VehicleScenarioLayoutInfos><Item><Name>LOCAL_SCENARIO</Name></Item></VehicleScenarioLayoutInfos>" +
            "</CVehicleMetadataMgr>\r\n";
        byte[] Bytes(string value) => encoding.GetPreamble().Concat(encoding.GetBytes(value)).ToArray();
        var original = Bytes(layout);
        w.Write("input/data/layouts.meta", original);
        var vehicles = PackageFixture.Vehicles(("car", "SULTAN")).Replace("</Item>",
            "<layout>car</layout><coverBoundOffsets>LOCAL_COVER</coverBoundOffsets><explosionInfo>LOCAL_EXPLOSION</explosionInfo><scenarioLayout>LOCAL_SCENARIO</scenarioLayout></Item>");
        w.WriteText("input/data/vehicles.meta", vehicles);
        var converter = new VehicleConverter(temporaryParent:w.PathFor("temp"));
        using var session = await converter.AnalyzeAsync([input]);
        var package = Assert.Single(session.Packages);
        Assert.True(package.CanExport, string.Join(";", package.Diagnostics.Select(d=>d.Message)));
        var result = await converter.ExportAsync(package, new("isolated", w.PathFor("out"),
            new Dictionary<string,string> { [package.Vehicles[0].Key] = "CUSTOM" }));
        var outputPath = Path.Combine(result.ResourceDirectory,"data/layouts.meta");
        var xml = XDocument.Load(outputPath);
        var isolatedSeat = xml.Descendants("Name").Single(e=>e.Value.Trim().EndsWith("_LOCAL_SEAT")).Value.Trim();
        var prefix = isolatedSeat[..^"LOCAL_SEAT".Length];
        var expected = layout.Replace("> LOCAL_SEAT <", "> " + prefix + "LOCAL_SEAT <")
            .Replace(">LOCAL_SEAT<", ">" + prefix + "LOCAL_SEAT<")
            .Replace("ref = 'LOCAL_SEAT'", "ref = '" + prefix + "LOCAL_SEAT'")
            .Replace(">LOCAL_ANIM<", ">" + prefix + "LOCAL_ANIM<")
            .Replace("ref=\"LOCAL_ANIM\"", "ref=\"" + prefix + "LOCAL_ANIM\"")
            .Replace("<Name>car</Name>", "<Name>" + prefix + "car</Name>");
        foreach (var name in new[]{"LOCAL_COVER","LOCAL_EXPLOSION","LOCAL_SCENARIO"})
            expected=expected.Replace("<Name>"+name+"</Name>","<Name>"+prefix+name+"</Name>");
        Assert.Equal(Bytes(expected), await File.ReadAllBytesAsync(outputPath));
        var expectedVehicles = vehicles.Replace("<layout>car</layout>","<layout>"+prefix+"car</layout>")
            .Replace(">SULTAN<",">CUSTOM<");
        foreach (var name in new[]{"LOCAL_COVER","LOCAL_EXPLOSION","LOCAL_SCENARIO"})
            expectedVehicles=expectedVehicles.Replace(">"+name+"<",">"+prefix+name+"<");
        Assert.Equal(Encoding.UTF8.GetBytes(expectedVehicles),await File.ReadAllBytesAsync(Path.Combine(result.ResourceDirectory,"data/vehicles.meta")));
        Assert.Equal(original,await File.ReadAllBytesAsync(w.PathFor("input/data/layouts.meta")));
        Assert.Equal(vehicles,await File.ReadAllTextAsync(w.PathFor("input/data/vehicles.meta")));
        var opened=await converter.OpenPackAsync(result.ResourceDirectory); using var reopened=opened.Session;
        var request=new PackageExportRequest(Assert.Single(reopened.Packages),"renamed",new Dictionary<string,string>());
        await converter.UpdatePackAsync(opened.Document,[request]);
        outputPath=Path.Combine(result.ResourceDirectory,"data/renamed/layouts.meta");
        Assert.Equal(Bytes(expected),await File.ReadAllBytesAsync(outputPath));
        var openedAgain=await converter.OpenPackAsync(result.ResourceDirectory); using var sessionAgain=openedAgain.Session;
        await using var stable=await sessionAgain.Packages[0].Files.Single(f=>f.DataFileType=="VEHICLE_LAYOUTS_FILE").Content.OpenReadAsync();
        using var memory=new MemoryStream(); await stable.CopyToAsync(memory);
        Assert.Equal(Bytes(expected),memory.ToArray());
    }

    [Fact]
    public async Task ConflictingDefinitionsInsideOnePackageAreStillBlocked()
    {
        using var w=new TestWorkspace(); var input=PackageFixture.WriteFolder(w,"input");
        w.WriteText("input/data/layouts.meta","<CVehicleMetadataMgr><VehicleSeatInfos><Item><Name>SAME</Name><SeatBoneName>left</SeatBoneName></Item><Item><Name>SAME</Name><SeatBoneName>right</SeatBoneName></Item></VehicleSeatInfos></CVehicleMetadataMgr>");
        var converter=new VehicleConverter(temporaryParent:w.PathFor("temp"));
        using var session=await converter.AnalyzeAsync([input]);
        var result=await converter.ValidatePackAsync([new(session.Packages[0],"one",new Dictionary<string,string>())]);
        Assert.False(result.CanExport);
        Assert.False(session.Packages[0].CanExport);
        Assert.Contains(session.Packages[0].Diagnostics,d=>d.Severity==DiagnosticSeverity.Error && d.Message.Contains("layout conflitante"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>converter.ExportAsync(session.Packages[0],new("blocked",w.PathFor("out"),new Dictionary<string,string>())));
    }

    [Fact]
    public async Task ExistingPackMigratesOnSaveWithoutOriginalDlcAndWithoutChangingAssets()
    {
        using var w=new TestWorkspace();var folder=PackageFixture.WriteFolder(w,"input");
        const string layout="<CVehicleMetadataMgr><VehicleLayoutInfos><Item><Name>OLD_RHD</Name></Item></VehicleLayoutInfos></CVehicleMetadataMgr>";
        var vehicles=PackageFixture.Vehicles(("car","SULTAN")).Replace("</Item>","<layout>OLD_RHD</layout></Item>");
        w.WriteText("input/data/layouts.meta",layout);w.WriteText("input/data/vehicles.meta",vehicles);
        var converter=new VehicleConverter(temporaryParent:w.PathFor("temp"));
        using var source=await converter.AnalyzeAsync([folder]);
        var exported=await converter.ExportPackAsync([new(source.Packages[0],"origin",new Dictionary<string,string>())],new("legacy",w.PathFor("out")));
        var layouts=Path.Combine(exported.ResourceDirectory,"data/origin/layouts.meta");
        var models=Path.Combine(exported.ResourceDirectory,"data/origin/vehicles.meta");
        // Simulate a version-1 pack created before layout isolation was implemented.
        await File.WriteAllTextAsync(layouts,layout);await File.WriteAllTextAsync(models,vehicles);
        var asset=Directory.GetFiles(Path.Combine(exported.ResourceDirectory,"stream"),"*.yft",SearchOption.AllDirectories).Single();
        var timestamp=File.GetLastWriteTimeUtc(asset);var hash=SHA256.HashData(await File.ReadAllBytesAsync(asset));
        var opened=await converter.OpenPackAsync(exported.ResourceDirectory);using var session=opened.Session;
        Assert.Equal(layout,await File.ReadAllTextAsync(layouts)); // Opening is read-only.
        await converter.UpdatePackAsync(opened.Document,[new(session.Packages[0],"origin",new Dictionary<string,string>())]);
        Assert.Contains("FVLI_",await File.ReadAllTextAsync(layouts));
        Assert.Contains("FVLI_",await File.ReadAllTextAsync(models));
        Assert.Equal(timestamp,File.GetLastWriteTimeUtc(asset));
        Assert.Equal(hash,SHA256.HashData(await File.ReadAllBytesAsync(asset)));
    }

    [Fact]
    public async Task ApprovedXmlRepairAlsoIsolatesItsLayouts()
    {
        using var w=new TestWorkspace();var folder=PackageFixture.WriteFolder(w,"input");
        w.WriteText("input/data/carcols.meta",MetadataRepairTests.Broken);
        w.WriteText("input/data/layouts.meta","<CVehicleMetadataMgr><VehicleLayoutInfos><Item><Name>SHARED</Name></Item></VehicleLayoutInfos></CVehicleMetadataMgr>");
        var converter=new VehicleConverter(temporaryParent:w.PathFor("temp"));
        using var session=await converter.AnalyzeAsync([folder]);
        var original=Assert.Single(session.Packages);Assert.False(original.CanExport);
        var repaired=Assert.IsType<PackageRepairProposal>(original.SuggestedRepair).RepairedPackage;
        var exported=await converter.ExportAsync(repaired,new("repaired",w.PathFor("out"),new Dictionary<string,string>()));
        Assert.Contains("FVLI_",await File.ReadAllTextAsync(Path.Combine(exported.ResourceDirectory,"data/layouts.meta")));
    }
    [LocalRhdFact]
    public async Task RealS14AndSixConflictingModsExportWithDisjointDefinitionsAndStableReimport()
    {
        using var w=new TestWorkspace();
        var folder=Environment.GetEnvironmentVariable("FIVEM_RHD_SAMPLE_DIRECTORY")!;
        var models=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"s14","chaser90","er34","mk2100","nis13","nis15","s15naoki"};
        var converter=new VehicleConverter(temporaryParent:w.PathFor("temp"));
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var found=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths=folder.Split(';').SelectMany(Directory.GetFiles).Where(p=>new[]{"04fa59-","069812-","1990-toyota-","270d45-","2e55bc-","af0ba3-","drift-naoki-"}.Any(prefix=>Path.GetFileName(p).StartsWith(prefix,StringComparison.OrdinalIgnoreCase)));
        foreach(var archive in paths)
        {
            using var session=await converter.AnalyzeAsync([archive]);
            var package=Assert.Single(session.Packages);
            var model=Assert.Single(package.Vehicles).ModelName;
            Assert.Contains(model,models);found.Add(model);
            Assert.True(package.CanExport,string.Join(";",package.Diagnostics.Select(d=>d.Message)));
            var result=await converter.ExportAsync(package,new(model,w.PathFor("out"),new Dictionary<string,string>()));
            var originals=new Dictionary<string,byte[]>();
            foreach(var file in package.Files)
            {
                var bytes=await File.ReadAllBytesAsync(SafePaths.Within(result.ResourceDirectory,file.DestinationPath));
                originals[file.DestinationPath]=bytes;
                await using var source=await file.Content.OpenReadAsync();
                Assert.Equal(await SHA256.HashDataAsync(source),SHA256.HashData(bytes));
                if(file.DataFileType!="VEHICLE_LAYOUTS_FILE")continue;
                using var memory=new MemoryStream(bytes);var doc=XDocument.Load(memory);
                foreach(var name in doc.Root!.Elements().SelectMany(s=>s.Elements("Item")).Elements("Name").Select(e=>e.Value.Trim()))
                {
                    Assert.StartsWith("FVLI_",name);
                    Assert.True(names.Add(name),"Definição compartilhada: "+name);
                }
            }
            var opened=await converter.OpenPackAsync(result.ResourceDirectory);using var reopened=opened.Session;
            var again=Assert.Single(reopened.Packages);
            foreach(var file in again.Files)
            {
                await using var input=await file.Content.OpenReadAsync();using var memory=new MemoryStream();await input.CopyToAsync(memory);
                Assert.Equal(originals[file.DestinationPath],memory.ToArray());
            }
        }
        Assert.True(models.SetEquals(found),"Nem todos os sete modelos foram validados.");
    }
}

public sealed class LocalRhdFactAttribute : FactAttribute
{
    public LocalRhdFactAttribute()
    {
        if(Environment.GetEnvironmentVariable("FIVEM_RHD_SAMPLE_DIRECTORY") is not { Length: > 0 } folders || !folders.Split(';').All(Directory.Exists))
            Skip="Defina FIVEM_RHD_SAMPLE_DIRECTORY para testar os sete mods RHD locais sem distribuí-los.";
    }
}
