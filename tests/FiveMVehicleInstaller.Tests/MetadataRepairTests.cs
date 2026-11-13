using System.Security.Cryptography;
using System.Text;
using FiveMVehicleInstaller.Core;
using FiveMVehicleInstaller.Infrastructure;

namespace FiveMVehicleInstaller.Tests;

public sealed class MetadataRepairTests
{
    internal const string Broken = "<?xml version='1.0' encoding='UTF-8'?>\r\n<CVehicleModelInfoVarGlobal><Kits><Item><kitName>0_default_modkit</kitName><id value='0'/><visibleMods>\r\n      </linkMods><statMods/></Item></Kits></CVehicleModelInfoVarGlobal>\r\n";

    [Fact]
    public async Task ProposesOnlyValidatedByteCorrectionAndKeepsOriginalBlockedAndUntouched()
    {
        using var w = new TestWorkspace(); var folder = PackageFixture.WriteFolder(w, "input");
        var path = w.WriteText("input/data/carcols.meta", Broken);
        var converter = new VehicleConverter(temporaryParent: w.PathFor("temp"));
        using var session = await converter.AnalyzeAsync([folder]); var original = Assert.Single(session.Packages);
        Assert.False(original.CanExport);
        var proposal = Assert.IsType<PackageRepairProposal>(original.SuggestedRepair);
        Assert.Contains("</linkMods> por </visibleMods>", proposal.Description);
        await Assert.ThrowsAsync<InvalidOperationException>(() => converter.ExportAsync(original, new("blocked", w.PathFor("out"), new Dictionary<string,string>())));
        Assert.False(Directory.Exists(w.PathFor("out")));
        var repaired = proposal.RepairedPackage; Assert.True(repaired.CanExport);
        var result = await converter.ExportAsync(repaired, new("approved", w.PathFor("out"), new Dictionary<string,string>()));
        Assert.Equal(Encoding.UTF8.GetBytes(Broken.Replace("</linkMods>", "</visibleMods>")), await File.ReadAllBytesAsync(Path.Combine(result.ResourceDirectory, "data/carcols.meta")));
        Assert.Equal(Encoding.UTF8.GetBytes(Broken), await File.ReadAllBytesAsync(path));
        using var imported = await converter.AnalyzeAsync([result.ResourceDirectory]);
        Assert.True(Assert.Single(imported.Packages).CanExport);
    }

    [Theory]
    [InlineData("<CVehicleModelInfoVarGlobal><visibleMods></linkMods><broken></CVehicleModelInfoVarGlobal>")]
    [InlineData("<CVehicleModelInfoVarGlobal><visibleMods></linkMods><visibleMods></linkMods></CVehicleModelInfoVarGlobal>")]
    [InlineData("<OtherRoot><visibleMods></linkMods></OtherRoot>")]
    [InlineData("<CVehicleModelInfoVarGlobal><visibleMods></wrong></CVehicleModelInfoVarGlobal>")]
    public async Task DoesNotOfferGenericOrAmbiguousXmlRepairs(string xml)
    {
        using var w = new TestWorkspace(); var folder = PackageFixture.WriteFolder(w, "input");
        w.WriteText("input/data/carcols.meta", xml);
        using var session = await new VehicleConverter(temporaryParent:w.PathFor("temp")).AnalyzeAsync([folder]);
        var package = Assert.Single(session.Packages); Assert.False(package.CanExport); Assert.Null(package.SuggestedRepair);
    }

    [LocalCrestaFact]
    public async Task RealCrestaHasOriginalMalformedTagAndProposedCopyDiffersOnlyAtThatTag()
    {
        using var w = new TestWorkspace(); var converter = new VehicleConverter(temporaryParent:w.PathFor("temp"));
        using var session = await converter.AnalyzeAsync([Environment.GetEnvironmentVariable("FIVEM_CRESTA_ARCHIVE")!]);
        var original = Assert.Single(session.Packages); Assert.False(original.CanExport);
        Assert.Equal("cresta", Assert.Single(original.Vehicles).ModelName);
        Assert.Contains(original.Diagnostics, d => d.Message.Contains("visibleMods") && d.Message.Contains("linkMods"));
        var revised = Assert.IsType<PackageRepairProposal>(original.SuggestedRepair).RepairedPackage;
        Assert.True(revised.CanExport);
        var carcols = Assert.Single(revised.Files, f=>f.DataFileType == "CARCOLS_FILE");
        await using var stream = await carcols.Content.OpenReadAsync(); using var buffer = new MemoryStream(); await stream.CopyToAsync(buffer);
        var corrected = buffer.ToArray(); var text = Encoding.UTF8.GetString(corrected);
        var reconstructed = Encoding.UTF8.GetBytes(text.Replace("</visibleMods>","</linkMods>"));
        Assert.Equal("B82F01372FBE822F0FE7E0A19BC55DCE48DC0C2CB26CE0A932DB8510AE9620E3",Convert.ToHexString(SHA256.HashData(reconstructed)));
        var result = await converter.ExportAsync(revised,new("cresta",w.PathFor("out"),new Dictionary<string,string>()));
        foreach(var file in revised.Files)
        {
            await using var source = await file.Content.OpenReadAsync();
            var expected = await SHA256.HashDataAsync(source);
            await using var exported = File.OpenRead(SafePaths.Within(result.ResourceDirectory,file.DestinationPath));
            Assert.Equal(expected,await SHA256.HashDataAsync(exported));
        }
    }
}

public sealed class LocalCrestaFactAttribute : FactAttribute
{
    public LocalCrestaFactAttribute()
    {
        if(!File.Exists(Environment.GetEnvironmentVariable("FIVEM_CRESTA_ARCHIVE")))Skip="Defina FIVEM_CRESTA_ARCHIVE para validar o RAR local sem distribuí-lo.";
    }
}