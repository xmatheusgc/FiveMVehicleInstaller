using System.Security.Cryptography;
using System.Text;
using FiveMVehicleInstaller.Core;
using FiveMVehicleInstaller.Infrastructure;

namespace FiveMVehicleInstaller.Tests;

public sealed class MetadataPaddingTests
{
    [Theory]
    [InlineData("utf8",false)]
    [InlineData("utf8",true)]
    [InlineData("utf16le",true)]
    [InlineData("utf16le",false)]
    [InlineData("utf16be",true)]
    [InlineData("utf16be",false)]
    [InlineData("utf32le",true)]
    [InlineData("utf32le",false)]
    [InlineData("utf32be",true)]
    [InlineData("utf32be",false)]
    [InlineData("latin1",false)]
    public async Task AcceptsOnlyTerminalNullCharactersAndPreservesEncodingAndAudioEditing(string format,bool bom)
    {
        using var w=new TestWorkspace();var folder=PackageFixture.WriteFolder(w,"input");
        Encoding encoding=format switch
        {
            "utf16le"=>new UnicodeEncoding(false,bom),"utf16be"=>new UnicodeEncoding(true,bom),
            "utf32le"=>new UTF32Encoding(false,bom),"utf32be"=>new UTF32Encoding(true,bom),
            "latin1"=>Encoding.Latin1,_=>new UTF8Encoding(bom)
        };
        var xml="<?xml version='1.0' encoding='"+encoding.WebName+"'?>\r\n<!-- ação -->\r\n"+
            System.Text.RegularExpressions.Regex.Replace(PackageFixture.Vehicles(("car","SULTAN")), @"\A<\?xml[^?]*\?>", "")+"\r\n<!-- after root -->\r\n";
        byte[] Bytes(string text)=>encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();
        var original=Bytes(xml+new string('\0',5));
        w.Write("input/data/vehicles.meta",original);
        var converter=new VehicleConverter(temporaryParent:w.PathFor("temp"));
        using var session=await converter.AnalyzeAsync([folder]);
        var package=Assert.Single(session.Packages);
        Assert.True(package.CanExport,string.Join(";",package.Diagnostics.Select(d=>d.Message)));
        Assert.Null(package.SuggestedRepair);
        Assert.Contains(package.Diagnostics,d=>d.Severity==DiagnosticSeverity.Information&&d.Message.Contains("preenchimento NUL"));
        var result=await converter.ExportAsync(package,new("compatible",w.PathFor("out"),new Dictionary<string,string>()));
        var target=Path.Combine(result.ResourceDirectory,"data/vehicles.meta");
        Assert.Equal(Bytes(xml),await File.ReadAllBytesAsync(target));
        var edited=await converter.ExportAsync(package,new("edited",w.PathFor("out"),new Dictionary<string,string>{[package.Vehicles[0].Key]="CUSTOM"}));
        Assert.Equal(Bytes(xml.Replace(">SULTAN<",">CUSTOM<")),await File.ReadAllBytesAsync(Path.Combine(edited.ResourceDirectory,"data/vehicles.meta")));
        Assert.Equal(original,await File.ReadAllBytesAsync(w.PathFor("input/data/vehicles.meta")));
        var opened=await converter.OpenPackAsync(result.ResourceDirectory);using var reopened=opened.Session;
        Assert.DoesNotContain(reopened.Packages[0].Diagnostics,d=>d.Message.Contains("preenchimento NUL"));
    }

    [Theory]
    [InlineData("<CVehicleModelInfo__InitDataList><InitDatas>\0</InitDatas></CVehicleModelInfo__InitDataList>\0")]
    [InlineData("<CVehicleModelInfo__InitDataList>\0")]
    [InlineData("<CVehicleModelInfo__InitDataList/><other/>\0")]
    [InlineData("<CVehicleModelInfo__InitDataList/>garbage\0")]
    [InlineData("\0<CVehicleModelInfo__InitDataList/>")]
    [InlineData("<CVehicleModelInfo__InitDataList>\u0001</CVehicleModelInfo__InitDataList>\0")]
    [InlineData("<CVehicleModelInfo__InitDataList><broken></CVehicleModelInfo__InitDataList>\0")]
    public async Task StillBlocksMalformedXmlAndInternalNulls(string xml)
    {
        using var w=new TestWorkspace();var folder=PackageFixture.WriteFolder(w,"input");
        var original=Encoding.UTF8.GetBytes(xml);w.Write("input/data/vehicles.meta",original);
        using var session=await new VehicleConverter(temporaryParent:w.PathFor("temp")).AnalyzeAsync([folder]);
        var package=Assert.Single(session.Packages);Assert.False(package.CanExport);
        Assert.Contains(package.Diagnostics,d=>d.Severity==DiagnosticSeverity.Error&&d.Message.Contains("XML inválido"));
        Assert.Equal(original,await File.ReadAllBytesAsync(w.PathFor("input/data/vehicles.meta")));
    }

    [Fact]
    public async Task HandlesPaddingInHandlingAndLayoutsBeforeLayoutIsolation()
    {
        using var w=new TestWorkspace();var folder=PackageFixture.WriteFolder(w,"input");
        var handlingPath=w.PathFor("input/data/handling.meta");
        var handling=await File.ReadAllBytesAsync(handlingPath);
        w.Write("input/data/handling.meta",handling.Concat(new byte[3]).ToArray());
        w.WriteText("input/data/layouts.meta","<CVehicleMetadataMgr><VehicleLayoutInfos><Item><Name>LOCAL</Name></Item></VehicleLayoutInfos></CVehicleMetadataMgr>\0\0");
        var converter=new VehicleConverter(temporaryParent:w.PathFor("temp"));using var session=await converter.AnalyzeAsync([folder]);
        var package=Assert.Single(session.Packages);Assert.True(package.CanExport);
        Assert.Equal(2,package.Diagnostics.Count(d=>d.Message.Contains("preenchimento NUL")));
        var output=await converter.ExportAsync(package,new("test",w.PathFor("out"),new Dictionary<string,string>()));
        Assert.Equal(handling,await File.ReadAllBytesAsync(Path.Combine(output.ResourceDirectory,"data/handling.meta")));
        Assert.Contains("FVLI_",await File.ReadAllTextAsync(Path.Combine(output.ResourceDirectory,"data/layouts.meta")));
    }

    [LocalSc300Fact]
    public async Task RealSc300ExportsWithoutRepairPromptAndPreservesOriginalArchive()
    {
        using var w=new TestWorkspace();var archive=Environment.GetEnvironmentVariable("FIVEM_SC300_ARCHIVE")!;
        var before=SHA256.HashData(await File.ReadAllBytesAsync(archive));
        var converter=new VehicleConverter(temporaryParent:w.PathFor("temp"));using var session=await converter.AnalyzeAsync([archive]);
        var package=Assert.Single(session.Packages);Assert.True(package.CanExport,string.Join(";",package.Diagnostics.Select(d=>d.Message)));
        Assert.Equal("sc300a",Assert.Single(package.Vehicles).ModelName);Assert.Null(package.SuggestedRepair);
        Assert.Contains(package.Diagnostics,d=>d.Message.Contains("5 byte(s) de preenchimento NUL"));
        var exported=await converter.ExportAsync(package,new("sc300",w.PathFor("out"),new Dictionary<string,string>()));
        Assert.Equal(48,package.Files.Count);
        var bytes=await File.ReadAllBytesAsync(Path.Combine(exported.ResourceDirectory,"data/vehicles.meta"));
        Assert.Equal(5567,bytes.Length);
        Assert.Equal("2C75D096CA7CDB07E54FE7EA53484DFC851FEB9033B30488F56DB50723BB3887",Convert.ToHexString(SHA256.HashData(bytes)));
        Assert.Equal(before,SHA256.HashData(await File.ReadAllBytesAsync(archive)));
        var opened=await converter.OpenPackAsync(exported.ResourceDirectory);using var reopened=opened.Session;
        Assert.True(Assert.Single(reopened.Packages).CanExport);
    }
}

public sealed class LocalSc300FactAttribute:FactAttribute
{
    public LocalSc300FactAttribute()
    {
        if(!File.Exists(Environment.GetEnvironmentVariable("FIVEM_SC300_ARCHIVE")))Skip="Defina FIVEM_SC300_ARCHIVE para validar o mod local sem distribuí-lo.";
    }
}