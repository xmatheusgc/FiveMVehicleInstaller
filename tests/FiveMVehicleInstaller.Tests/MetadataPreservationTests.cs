using System.Text;
using FiveMVehicleInstaller.Infrastructure;

namespace FiveMVehicleInstaller.Tests;

public sealed class MetadataPreservationTests
{
    [Theory]
    [InlineData("utf8", false, "\r\n")]
    [InlineData("utf8", true, "\n")]
    [InlineData("utf16le", true, "\r\n")]
    [InlineData("utf16be", true, "\r")]
    [InlineData("utf16le", false, "\n")]
    [InlineData("utf16be", false, "\r\n")]
    [InlineData("utf32le", true, "\n")]
    [InlineData("utf32be", false, "\r\n")]
    [InlineData("latin1", false, "\r\n")]
    public async Task SoundEditPreservesEveryOtherByte(string encodingName, bool bom, string newline)
    {
        using var workspace = new TestWorkspace();
        var input = PackageFixture.WriteFolder(workspace, "input");
        var encoding = EncodingFor(encodingName, bom);
        var xml = "<?xml version='1.0' encoding='" + encoding.WebName + "'?>" + newline +
            "<!-- ação: entities, tabs, quotes and empty elements must survive -->" + newline +
            "<v:CVehicleModelInfo__InitDataList xmlns:v='urn:test'>" + newline +
            "\t<v:InitDatas>" + newline +
            "  <v:Item><v:modelName>car</v:modelName><v:audioNameHash>SULTAN</v:audioNameHash>" +
            "<v:other a = 'A&#38;B' /><!-- </v:Item> --><![CDATA[<not-a-tag>]]></v:Item>" + newline +
            "  <v:Item><v:modelName>other</v:modelName><v:audioNameHash>SULTAN</v:audioNameHash>" +
            "<v:unchanged> A&#x26;B </v:unchanged></v:Item>" + newline +
            " </v:InitDatas>" + newline + "</v:CVehicleModelInfo__InitDataList>" + newline;
        var original = Bytes(encoding, xml);
        var source = workspace.Write("input/data/vehicles.meta", original);
        workspace.Write("input/other.yft", RpfFixture.Resource([9]));
        var converter = new VehicleConverter(temporaryParent: workspace.PathFor("temp"));
        using var analysis = await converter.AnalyzeAsync([input]);
        var package = Assert.Single(analysis.Packages);
        Assert.True(package.CanExport, string.Join(";", package.Diagnostics.Select(d => d.Message)));
        var result = await converter.ExportAsync(package, new("edited", workspace.PathFor("output"),
            new Dictionary<string, string> { [package.Vehicles[1].Key] = "A&B<ENGINE>" }));
        var position = xml.LastIndexOf(">SULTAN</", StringComparison.Ordinal);
        var expected = xml[..position] + ">A&amp;B&lt;ENGINE&gt;</" + xml[(position + ">SULTAN</".Length)..];
        Assert.Equal(Bytes(encoding, expected), await File.ReadAllBytesAsync(Path.Combine(result.ResourceDirectory, "data/vehicles.meta")));
        Assert.Equal(original, await File.ReadAllBytesAsync(source));
        foreach (var file in package.Files.Where(f => f.DataFileType is not null && f.DataFileType != "VEHICLE_METADATA_FILE"))
        {
            await using var stream = await file.Content.OpenReadAsync(default);
            using var buffer = new MemoryStream(); await stream.CopyToAsync(buffer);
            Assert.Equal(buffer.ToArray(), await File.ReadAllBytesAsync(SafePaths.Within(result.ResourceDirectory, file.DestinationPath)));
        }
    }

    [Theory]
    [InlineData("<audioNameHash />", "<audioNameHash >CUSTOM</audioNameHash>")]
    [InlineData("<audioNameHash/>", "<audioNameHash>CUSTOM</audioNameHash>")]
    [InlineData("<audioNameHash><![CDATA[SULTAN]]><!-- </audioNameHash> --></audioNameHash>", "<audioNameHash>CUSTOM</audioNameHash>")]
    [InlineData("<audioNameHash> \tSULTAN\r\n </audioNameHash>", "<audioNameHash> \tCUSTOM\r\n </audioNameHash>")]
    [InlineData("", "")]
    public async Task EditsEmptyOrMissingSoundWithoutSerializingItem(string audio, string expectedAudio)
    {
        using var workspace = new TestWorkspace(); var input = PackageFixture.WriteFolder(workspace, "input");
        var xml = "<?xml version='1.0'?>\r\n<CVehicleModelInfo__InitDataList><InitDatas>" +
            "<Item attr = 'keep > this'><modelName>car</modelName>" + audio +
            "<!-- </Item> --><other><![CDATA[</Item>]]></other>\t</Item>" +
            "</InitDatas></CVehicleModelInfo__InitDataList>\r\n";
        workspace.WriteText("input/data/vehicles.meta", xml);
        var converter = new VehicleConverter(temporaryParent: workspace.PathFor("temp"));
        using var analysis = await converter.AnalyzeAsync([input]); var package = Assert.Single(analysis.Packages);
        var result = await converter.ExportAsync(package, new("edited", workspace.PathFor("output"),
            new Dictionary<string, string> { [package.Vehicles[0].Key] = "CUSTOM" }));
        var expected = audio.Length > 0 ? xml.Replace(audio, expectedAudio, StringComparison.Ordinal) :
            xml.Replace("\t</Item>", "\t<audioNameHash>CUSTOM</audioNameHash></Item>", StringComparison.Ordinal);
        Assert.Equal(Encoding.UTF8.GetBytes(expected), await File.ReadAllBytesAsync(Path.Combine(result.ResourceDirectory, "data/vehicles.meta")));
    }

    [Fact]
    public async Task NoSoundChangePreservesEntitiesBomAndFormattingExactly()
    {
        using var workspace = new TestWorkspace(); var input = PackageFixture.WriteFolder(workspace, "input");
        var xml = PackageFixture.Vehicles(("car", "SULT&#65;N"));
        var bytes = Bytes(new UTF8Encoding(true), xml);
        workspace.Write("input/data/vehicles.meta", bytes);
        var converter = new VehicleConverter(temporaryParent: workspace.PathFor("temp"));
        using var analysis = await converter.AnalyzeAsync([input]); var package = Assert.Single(analysis.Packages);
        var result = await converter.ExportAsync(package, new("same", workspace.PathFor("output"),
            new Dictionary<string, string> { [package.Vehicles[0].Key] = " SULTAN " }));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(result.ResourceDirectory, "data/vehicles.meta")));
    }

    [Fact]
    public async Task ChangesSeveralVehiclesWithoutOffsetDrift()
    {
        using var workspace = new TestWorkspace(); var input = PackageFixture.WriteFolder(workspace, "input");
        var xml = PackageFixture.Vehicles(("car", "SULTAN"), ("other", "ADDER"));
        workspace.WriteText("input/data/vehicles.meta", xml); workspace.Write("input/other.yft", RpfFixture.Resource([9]));
        var converter = new VehicleConverter(temporaryParent: workspace.PathFor("temp"));
        using var analysis = await converter.AnalyzeAsync([input]); var package = Assert.Single(analysis.Packages);
        var changes = new Dictionary<string, string> { [package.Vehicles[1].Key] = "", [package.Vehicles[0].Key] = "LONG_CUSTOM_ENGINE" };
        var result = await converter.ExportAsync(package, new("both", workspace.PathFor("output"), changes));
        Assert.Equal(Encoding.UTF8.GetBytes(xml.Replace(">SULTAN<", ">LONG_CUSTOM_ENGINE<").Replace(">ADDER<", "><")),
            await File.ReadAllBytesAsync(Path.Combine(result.ResourceDirectory, "data/vehicles.meta")));
    }

    private static byte[] Bytes(Encoding encoding, string value) => [.. encoding.GetPreamble(), .. encoding.GetBytes(value)];
    private static Encoding EncodingFor(string name, bool bom) => name switch
    {
        "utf8" => new UTF8Encoding(bom),
        "utf16le" => new UnicodeEncoding(false, bom),
        "utf16be" => new UnicodeEncoding(true, bom),
        "utf32le" => new UTF32Encoding(false, bom),
        "utf32be" => new UTF32Encoding(true, bom),
        "latin1" => Encoding.Latin1,
        _ => throw new ArgumentException(name)
    };
}
