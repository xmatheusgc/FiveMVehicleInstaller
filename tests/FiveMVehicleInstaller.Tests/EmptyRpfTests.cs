using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using FiveMVehicleInstaller.Core;
using FiveMVehicleInstaller.Infrastructure;

namespace FiveMVehicleInstaller.Tests;

public sealed class EmptyRpfTests
{
    private const string EmptyPath="x64/levels/car/vehiclemods/car_mods.rpf";
    private static string Content=>"<CDataFileMgr__ContentsOfDataFileXml><dataFiles>"+
        "<Item><filename>dlc_car:/data/vehicles.meta</filename><fileType>VEHICLE_METADATA_FILE</fileType></Item>"+
        "<Item><filename>dlc_car:/data/handling.meta</filename><fileType>HANDLING_FILE</fileType></Item>"+
        "<Item><filename>dlc_car:/x64/vehicles.rpf</filename><fileType>RPF_FILE</fileType></Item>"+
        "<Item><filename>dlc_car:/"+EmptyPath+"</filename><fileType>RPF_FILE</fileType></Item>"+
        "</dataFiles></CDataFileMgr__ContentsOfDataFileXml>";

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task AcceptsDeclaredEmptyRpfInFoldersAndNestedDlcWithoutExportingContainers(bool nested)
    {
        using var w=new TestWorkspace();
        var empty=RpfFixture.Create();Assert.Equal(512,empty.Length);
        var files=PackageFixture.Files().Select(f=>f.Path=="content.xml"?new FixtureFile(f.Path,Encoding.UTF8.GetBytes(Content)):f)
            .Append(new FixtureFile(EmptyPath,empty)).ToArray();
        string input;
        if(nested)input=w.Write("dlc.rpf",RpfFixture.Create(files));
        else{foreach(var file in files)w.Write("input/"+file.Path,file.Data);input=w.PathFor("input");}
        var converter=new VehicleConverter(temporaryParent:w.PathFor("temp"));using var session=await converter.AnalyzeAsync([input]);
        var package=Assert.Single(session.Packages);Assert.True(package.CanExport,string.Join(";",package.Diagnostics.Select(d=>d.Message)));
        Assert.Contains(package.Diagnostics,d=>d.Severity==DiagnosticSeverity.Information&&d.Message.Contains("RPF referenciado encontrado e validado"));
        Assert.DoesNotContain(package.Files,f=>f.DestinationPath.EndsWith(".rpf",StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2,package.Files.Count(f=>f.DataFileType is null));
        var exported=await converter.ExportAsync(package,new("accepted",w.PathFor("out"),new Dictionary<string,string>()));
        Assert.Empty(Directory.GetFiles(exported.ResourceDirectory,"*.rpf",SearchOption.AllDirectories));
        Assert.DoesNotContain(".rpf",await File.ReadAllTextAsync(Path.Combine(exported.ResourceDirectory,"fxmanifest.lua")));
        var opened=await converter.OpenPackAsync(exported.ResourceDirectory);using var reopened=opened.Session;
        Assert.True(Assert.Single(reopened.Packages).CanExport);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task MissingOrCorruptRpfRemainsBlocked(bool corrupt)
    {
        using var w=new TestWorkspace();var input=PackageFixture.WriteFolder(w,"input");
        w.WriteText("input/content.xml",Content);
        if(corrupt)w.Write("input/"+EmptyPath,new byte[512]);
        var converter=new VehicleConverter(temporaryParent:w.PathFor("temp"));using var session=await converter.AnalyzeAsync([input]);
        var package=Assert.Single(session.Packages);Assert.False(package.CanExport);
        Assert.Contains(package.Diagnostics,d=>d.Severity==DiagnosticSeverity.Error&&d.Message.Contains(EmptyPath));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>converter.ExportAsync(package,new("blocked",w.PathFor("out"),new Dictionary<string,string>())));
        Assert.False(Directory.Exists(w.PathFor("out")));
    }

    [LocalEmptyChaserFact]
    public async Task RealChaserRecognizesEmptyModsRpfAndExportsEveryAssetUnchanged()
    {
        using var w=new TestWorkspace();var archive=Environment.GetEnvironmentVariable("FIVEM_EMPTY_CHASER_ARCHIVE")!;
        var before=SHA256.HashData(await File.ReadAllBytesAsync(archive));
        var converter=new VehicleConverter(temporaryParent:w.PathFor("temp"));using var session=await converter.AnalyzeAsync([archive]);
        var package=Assert.Single(session.Packages);Assert.True(package.CanExport,string.Join(";",package.Diagnostics.Select(d=>d.Message)));
        Assert.Equal("chaser",Assert.Single(package.Vehicles).ModelName);
        Assert.Contains(package.Diagnostics,d=>d.Severity==DiagnosticSeverity.Information&&d.Message.Contains("chaser_mods.rpf"));
        Assert.Equal(8,package.Files.Count);Assert.Equal(3,package.Files.Count(f=>f.DataFileType is null));
        var exported=await converter.ExportAsync(package,new("chaser",w.PathFor("out"),new Dictionary<string,string>()));
        using var zip=ZipFile.OpenRead(archive);
        var originals=zip.Entries.Where(e=>e.Length>0&&new[]{".yft",".ytd"}.Contains(Path.GetExtension(e.FullName)))
            .ToDictionary(e=>Path.GetFileName(e.FullName),StringComparer.OrdinalIgnoreCase);
        foreach(var file in package.Files)
        {
            var path=SafePaths.Within(exported.ResourceDirectory,file.DestinationPath);
            await using var actual=File.OpenRead(path);await using var source=await file.Content.OpenReadAsync();
            Assert.Equal(await SHA256.HashDataAsync(source),await SHA256.HashDataAsync(actual));
            if(file.DataFileType is null)
            {
                var original=originals[Path.GetFileName(file.DestinationPath)];await using var raw=original.Open();
                Assert.Equal(await SHA256.HashDataAsync(raw),SHA256.HashData(await File.ReadAllBytesAsync(path)));
            }
        }
        Assert.Equal(before,SHA256.HashData(await File.ReadAllBytesAsync(archive)));
        var opened=await converter.OpenPackAsync(exported.ResourceDirectory);using var reopened=opened.Session;
        Assert.True(Assert.Single(reopened.Packages).CanExport);
    }
}

public sealed class LocalEmptyChaserFactAttribute:FactAttribute
{
    public LocalEmptyChaserFactAttribute()
    {
        if(!File.Exists(Environment.GetEnvironmentVariable("FIVEM_EMPTY_CHASER_ARCHIVE")))Skip="Defina FIVEM_EMPTY_CHASER_ARCHIVE para testar o Chaser com RPF de tuning vazio.";
    }
}