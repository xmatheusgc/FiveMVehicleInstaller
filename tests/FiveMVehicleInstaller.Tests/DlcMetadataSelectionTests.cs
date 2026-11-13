using System.Text.RegularExpressions;
using FiveMVehicleInstaller.Core;
using FiveMVehicleInstaller.Infrastructure;

namespace FiveMVehicleInstaller.Tests;

public sealed class DlcMetadataSelectionTests
{
    [Theory]
    [InlineData("handling.meta",false)]
    [InlineData("custom-physics.meta",false)]
    [InlineData("handling.meta",true)]
    public async Task ExportsOnlyActiveMetadataIncludingCustomFilenamesAndIgnoresInvalidUnusedAlternatives(string selected,bool invalidAlternative)
    {
        using var w=new TestWorkspace(); var input=PackageFixture.WriteFolder(w,"input");
        var original=Handling("TEST",1);
        w.WriteText("input/data/"+selected,original);
        w.WriteText("input/data/handlingb.meta",Handling("TEST",2));
        w.WriteText("input/data/handlingstock.meta",invalidAlternative?"<broken>":Handling("TEST",3));
        w.WriteText("input/setup2.xml","<SSetupData><nameHash>car</nameHash><contentChangeSetGroups><Item><NameHash>GROUP_STARTUP</NameHash><ContentChangeSets><Item>MAIN</Item></ContentChangeSets></Item></contentChangeSetGroups></SSetupData>");
        w.WriteText("input/content.xml",$"<CDataFileMgr__ContentsOfDataFileXml><dataFiles>{Reference("vehicles.meta","VEHICLE_METADATA_FILE",true)}{Reference(selected,"HANDLING_FILE",true)}{Reference("handlingb.meta","HANDLING_FILE",true)}</dataFiles><contentChangeSets><Item><changeSetName>MAIN</changeSetName><filesToEnable><Item>dlc_test:/data/vehicles.meta</Item><Item>dlc_test:/data/{selected}</Item></filesToEnable></Item><Item><changeSetName>ALTERNATIVE</changeSetName><filesToEnable><Item>dlc_test:/data/handlingb.meta</Item></filesToEnable></Item></contentChangeSets></CDataFileMgr__ContentsOfDataFileXml>");
        var converter=new VehicleConverter(temporaryParent:w.PathFor("temp"));
        using var session=await converter.AnalyzeAsync([input]);var package=Assert.Single(session.Packages);
        Assert.True(package.CanExport,string.Join(";",package.Diagnostics.Select(d=>d.Message)));
        Assert.Equal("data/"+selected,Assert.Single(package.Files,f=>f.DataFileType=="HANDLING_FILE").DestinationPath);
        Assert.Contains(package.Diagnostics,d=>d.Message.Contains("não ativado pelo DLC")&&d.Message.Contains("handlingb.meta"));
        var export=await converter.ExportAsync(package,new("active",w.PathFor("out"),new Dictionary<string,string>()));
        var manifest=await File.ReadAllTextAsync(Path.Combine(export.ResourceDirectory,"fxmanifest.lua"));
        Assert.Single(Regex.Matches(manifest,"(?m)^data_file 'HANDLING_FILE'"));
        Assert.False(File.Exists(Path.Combine(export.ResourceDirectory,"data/handlingb.meta")));
        Assert.False(File.Exists(Path.Combine(export.ResourceDirectory,"data/handlingstock.meta")));
        Assert.Equal(original,await File.ReadAllTextAsync(Path.Combine(export.ResourceDirectory,"data/"+selected)));
        Assert.Equal(invalidAlternative?"<broken>":Handling("TEST",3),await File.ReadAllTextAsync(Path.Combine(input,"data/handlingstock.meta")));
    }

    [Fact]
    public async Task KeepsInitiallyEnabledFilesEvenWhenOtherFilesAreExplicitlyEnabledAndHonorsDisableList()
    {
        using var w=new TestWorkspace();var input=PackageFixture.WriteFolder(w,"input");
        w.WriteText("input/data/handling.meta",Handling("TEST",1));
        w.WriteText("input/data/handlingb.meta",Handling("TEST",2));
        w.WriteText("input/content.xml",$"<CDataFileMgr__ContentsOfDataFileXml><dataFiles>{Reference("vehicles.meta","VEHICLE_METADATA_FILE",false)}{Reference("handling.meta","HANDLING_FILE",false)}{Reference("handlingb.meta","HANDLING_FILE",false)}</dataFiles><contentChangeSets><Item><filesToEnable><Item>dlc_test:/data/handling.meta</Item></filesToEnable><filesToDisable><Item>dlc_test:/data/handlingb.meta</Item></filesToDisable></Item></contentChangeSets></CDataFileMgr__ContentsOfDataFileXml>");
        var converter=new VehicleConverter(temporaryParent:w.PathFor("temp"));
        using var session=await converter.AnalyzeAsync([input]);var package=Assert.Single(session.Packages);
        Assert.True(package.CanExport);Assert.Single(package.Vehicles);
        Assert.EndsWith("/handling.meta",Assert.Single(package.Files,f=>f.DataFileType=="HANDLING_FILE").DestinationPath);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task BlocksAmbiguousConflictingHandlingWithoutChoosingByFilename(bool ready)
    {
        using var w=new TestWorkspace();var input=PackageFixture.WriteFolder(w,"input");
        w.WriteText("input/data/handling.meta",Handling("TEST",1));w.WriteText("input/data/handlingb.meta",Handling("TEST",2));
        if(ready)
        {
            w.WriteText("input/setup2.xml","<SSetupData><nameHash>car</nameHash></SSetupData>");
            w.WriteText("input/content.xml",$"<CDataFileMgr__ContentsOfDataFileXml><dataFiles>{Reference("vehicles.meta","VEHICLE_METADATA_FILE",false)}{Reference("handling.meta","HANDLING_FILE",false)}{Reference("handlingb.meta","HANDLING_FILE",false)}</dataFiles></CDataFileMgr__ContentsOfDataFileXml>");
            w.Write("input/stream/car.yft",RpfFixture.Resource([1]));
        }
        var converter=new VehicleConverter(temporaryParent:w.PathFor("temp"));using var session=await converter.AnalyzeAsync([input]);
        var package=Assert.Single(session.Packages);Assert.False(package.CanExport);
        Assert.Contains(package.Diagnostics,d=>d.Severity==DiagnosticSeverity.Error&&d.Message.Contains("Handling conflitante TEST"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>converter.ExportAsync(package,new("blocked",w.PathFor("out"),new Dictionary<string,string>())));
        Assert.False(Directory.Exists(w.PathFor("out")));
    }

    [Fact]
    public async Task KeepsMultipleHandlingFilesForDistinctIdsWhenDeclaredByResource()
    {
        using var w=new TestWorkspace();
        w.WriteText("input/setup2.xml","<SSetupData><nameHash>car</nameHash></SSetupData>");
            w.WriteText("input/content.xml",$"<CDataFileMgr__ContentsOfDataFileXml><dataFiles>{Reference("vehicles.meta","VEHICLE_METADATA_FILE",false)}{Reference("handling.meta","HANDLING_FILE",false)}{Reference("handlingb.meta","HANDLING_FILE",false)}</dataFiles></CDataFileMgr__ContentsOfDataFileXml>");
        w.WriteText("input/data/vehicles.meta",PackageFixture.Vehicles(("car","SULTAN")));
        w.WriteText("input/data/handling.meta",Handling("TEST",1));w.WriteText("input/data/handlingb.meta",Handling("OTHER",2));
        w.Write("input/stream/car.yft",RpfFixture.Resource([1]));
        var converter=new VehicleConverter(temporaryParent:w.PathFor("temp"));using var session=await converter.AnalyzeAsync([w.PathFor("input")]);
        var package=Assert.Single(session.Packages);Assert.True(package.CanExport);
        Assert.Equal(2,package.Files.Count(f=>f.DataFileType=="HANDLING_FILE"));
        var export=await converter.ExportAsync(package,new("valid",w.PathFor("out"),new Dictionary<string,string>()));
        Assert.Equal(2,Regex.Matches(await File.ReadAllTextAsync(Path.Combine(export.ResourceDirectory,"fxmanifest.lua")),"(?m)^data_file 'HANDLING_FILE'").Count);
    }

    [Local180sxFact]
    public async Task Real180sxExportsOnlyOriginalActiveHandlingWithAllAssetBytesIntact()
    {
        using var w=new TestWorkspace();var converter=new VehicleConverter(temporaryParent:w.PathFor("temp"));
        using var session=await converter.AnalyzeAsync([Environment.GetEnvironmentVariable("FIVEM_180SX_ARCHIVE")!]);
        var package=Assert.Single(session.Packages);Assert.True(package.CanExport,string.Join(";",package.Diagnostics.Select(d=>d.Message)));
        Assert.Equal("180sx",Assert.Single(package.Vehicles).ModelName);
        Assert.Equal(169,package.Files.Count(f=>f.DataFileType is null));
        Assert.Equal(4,package.Files.Count(f=>f.DataFileType is not null));
        Assert.Equal("data/handling.meta",Assert.Single(package.Files,f=>f.DataFileType=="HANDLING_FILE").DestinationPath);
        var export=await converter.ExportAsync(package,new("180sx_test",w.PathFor("out"),new Dictionary<string,string>()));
        foreach(var file in package.Files)
        {
            await using var source=await file.Content.OpenReadAsync();using var data=new MemoryStream();await source.CopyToAsync(data);
            Assert.Equal(data.ToArray(),await File.ReadAllBytesAsync(SafePaths.Within(export.ResourceDirectory,file.DestinationPath)));
        }
        var manifest=await File.ReadAllTextAsync(Path.Combine(export.ResourceDirectory,"fxmanifest.lua"));
        Assert.Single(Regex.Matches(manifest,"(?m)^data_file 'HANDLING_FILE'"));
        Assert.DoesNotContain("handlingb.meta",manifest);Assert.DoesNotContain("handlingstock.meta",manifest);
        using var recovered=await converter.AnalyzeAsync([export.ResourceDirectory]);Assert.True(Assert.Single(recovered.Packages).CanExport);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task MissingStartupSetWarnsButNeverEnablesUndeclaredInactiveMetadata(bool inactive)
    {
        using var w=new TestWorkspace();var folder=PackageFixture.WriteFolder(w,"input");
        w.WriteText("input/setup2.xml","<SSetupData><nameHash>car</nameHash><contentChangeSetGroups><Item><NameHash>GROUP_STARTUP</NameHash><ContentChangeSets><Item>ABSENT_UNLOCKS</Item></ContentChangeSets></Item></contentChangeSetGroups></SSetupData>");
        w.WriteText("input/content.xml",$"<CDataFileMgr__ContentsOfDataFileXml><dataFiles>{Reference("vehicles.meta","VEHICLE_METADATA_FILE",inactive)}</dataFiles><contentChangeSets/></CDataFileMgr__ContentsOfDataFileXml>");
        using var session=await new VehicleConverter(temporaryParent:w.PathFor("temp")).AnalyzeAsync([folder]);
        var package=Assert.Single(session.Packages);Assert.Equal(!inactive,package.CanExport);
        Assert.Contains(package.Diagnostics,d=>d.Severity==DiagnosticSeverity.Warning&&d.Message.Contains("ABSENT_UNLOCKS"));
        Assert.DoesNotContain(package.Diagnostics,d=>d.Severity==DiagnosticSeverity.Error&&d.Message.Contains("ABSENT_UNLOCKS"));
        if(inactive)Assert.Empty(package.Vehicles);
    }
    private static string Reference(string filename,string type,bool disabled)=>$"<Item><filename>dlc_test:/data/{filename}</filename><fileType>{type}</fileType><disabled value='{disabled.ToString().ToLowerInvariant()}'/></Item>";
    private static string Handling(string id,int value)=>$"<CHandlingDataMgr><HandlingData><Item type='CHandlingData'><handlingName>{id}</handlingName><fMass value='{value}'/></Item></HandlingData></CHandlingDataMgr>";
}

public sealed class Local180sxFactAttribute:FactAttribute
{
    public Local180sxFactAttribute()
    {
        if(!File.Exists(Environment.GetEnvironmentVariable("FIVEM_180SX_ARCHIVE")))Skip="Defina FIVEM_180SX_ARCHIVE para validar o ZIP local sem distribuí-lo.";
    }
}