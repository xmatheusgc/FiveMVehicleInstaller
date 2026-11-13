using FiveMVehicleInstaller.Core;
using FiveMVehicleInstaller.Infrastructure;

namespace FiveMVehicleInstaller.Tests;

public sealed class LayoutCompatibilityTests
{
    [Theory]
    [InlineData("VehicleLayoutInfos")]
    [InlineData("VehicleSeatInfos")]
    [InlineData("VehicleSeatAnimInfos")]
    [InlineData("VehicleEntryPointInfos")]
    [InlineData("VehicleEntryPointAnimInfos")]
    public async Task IsolatesConflictingDefinitionsEvenWhenImportedSeparately(string section)
    {
        using var w=new TestWorkspace();
        var first=PackageFixture.WriteFolder(w,"first"); var second=PackageFixture.WriteFolder(w,"second","other");
        var original=Layout(section,"SHARED","seat_dside_f");
        w.WriteText("first/data/vehiclelayouts.meta",original);
        w.WriteText("second/data/vehiclelayouts.meta",Layout(section,"shared","seat_pside_f"));
        var converter=new VehicleConverter(temporaryParent:w.PathFor("temp"));
        using var session=await converter.AnalyzeAsync([first,second]);
        Assert.Equal(2,session.Packages.Count);
        Assert.All(session.Packages,p=>Assert.True(p.CanExport));
        Assert.DoesNotContain(session.Diagnostics,d=>d.Message.Contains("layout conflitante"));
        var requests=session.Packages.Select((p,i)=>new PackageExportRequest(p,"origin"+i,new Dictionary<string,string>())).ToArray();
        var validation=await converter.ValidatePackAsync(requests);
        Assert.True(validation.CanExport);
        var pack=await converter.ExportPackAsync(requests,new("safe",w.PathFor("packs")));
        var opened=await converter.OpenPackAsync(pack.ResourceDirectory); using var reopened=opened.Session;
        Assert.Equal(2,reopened.Packages.Count);
        var result=await converter.ExportAsync(session.Packages[0],new("single",w.PathFor("single"),new Dictionary<string,string>()));
        var exported=await File.ReadAllTextAsync(Path.Combine(result.ResourceDirectory,"data/vehiclelayouts.meta"));
        Assert.Contains("FVLI_",exported);
        Assert.Equal(original,await File.ReadAllTextAsync(w.PathFor("first/data/vehiclelayouts.meta")));
        using var alone=await converter.AnalyzeAsync([first]);
        await using var stream=await alone.Packages[0].Files.Single(f=>f.DataFileType=="VEHICLE_LAYOUTS_FILE").Content.OpenReadAsync();
        using var reader=new StreamReader(stream);
        Assert.Equal(exported,await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task AllowsIdenticalSharedDefinitionsRegardlessOfIndentationCommentsAndAttributeOrder()
    {
        using var w=new TestWorkspace();
        var first=PackageFixture.WriteFolder(w,"first"); var second=PackageFixture.WriteFolder(w,"second","other");
        w.WriteText("first/data/vehiclelayouts.meta","<CVehicleMetadataMgr><VehicleSeatInfos><Item type='CVehicleSeatInfo'><Name>SHARED</Name><Offset x='1' y='2'/></Item></VehicleSeatInfos></CVehicleMetadataMgr>");
        w.WriteText("second/data/vehiclelayouts.meta","<CVehicleMetadataMgr>\r\n<VehicleSeatInfos>\n<!-- comment --><Item type='CVehicleSeatInfo'>\n<Name>SHARED</Name><Offset y='2' x='1'/>\n</Item></VehicleSeatInfos></CVehicleMetadataMgr>");
        var converter=new VehicleConverter(temporaryParent:w.PathFor("temp"));
        using var session=await converter.AnalyzeAsync([first,second]);
        Assert.DoesNotContain(session.Diagnostics,d=>d.Message.Contains("layout conflitante"));
        var requests=session.Packages.Select((p,i)=>new PackageExportRequest(p,"origin"+i,new Dictionary<string,string>())).ToArray();
        var result=await converter.ExportPackAsync(requests,new("safe",w.PathFor("packs")));
        Assert.True(File.Exists(Path.Combine(result.ResourceDirectory,"fxmanifest.lua")));
    }

    private static string Layout(string section,string name,string bone)=>
        $"<CVehicleMetadataMgr><{section}><Item><Name>{name}</Name><SeatBoneName>{bone}</SeatBoneName></Item></{section}></CVehicleMetadataMgr>";
}