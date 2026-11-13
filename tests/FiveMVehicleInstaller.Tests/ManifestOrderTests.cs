using System.Text.RegularExpressions;
using FiveMVehicleInstaller.Infrastructure;

namespace FiveMVehicleInstaller.Tests;

public sealed class ManifestOrderTests
{
    [Fact]
    public async Task LoadsAllVehicleDefinitionsBeforeKitsAndAllKitsBeforeVariations()
    {
        using var workspace = new TestWorkspace(); var input = PackageFixture.WriteFolder(workspace, "input");
        workspace.WriteText("input/data/z-other.meta", PackageFixture.Vehicles(("other", "ADDER")));
        workspace.Write("input/other.yft", RpfFixture.Resource([8]));
        workspace.WriteText("input/data/a-colors.meta", "<CVehicleModelInfoVarGlobal/>");
        workspace.WriteText("input/data/b-colors.meta", "<CVehicleModelInfoVarGlobal/>");
        workspace.WriteText("input/data/c-variations.meta", "<CVehicleModelInfoVariation/>");
        workspace.WriteText("input/data/d-variations.meta", "<CVehicleModelInfoVariation/>");
        workspace.WriteText("input/data/ai/layouts.meta", "<CVehicleMetadataMgr/>");
        var converter = new VehicleConverter(temporaryParent: workspace.PathFor("temp"));
        using var analysis = await converter.AnalyzeAsync([input]); var package = Assert.Single(analysis.Packages);
        Assert.True(package.CanExport);
        var result = await converter.ExportAsync(package, new("ordered", workspace.PathFor("output"), new Dictionary<string, string>()));
        var manifest = await File.ReadAllTextAsync(Path.Combine(result.ResourceDirectory, "fxmanifest.lua"));
        var declarations = Regex.Matches(manifest, "(?m)^data_file '([^']+)' '([^']+)'");
        Assert.Equal(new[]
        {
            "HANDLING_FILE", "VEHICLE_LAYOUTS_FILE",
            "VEHICLE_METADATA_FILE", "VEHICLE_METADATA_FILE",
            "CARCOLS_FILE", "CARCOLS_FILE",
            "VEHICLE_VARIATION_FILE", "VEHICLE_VARIATION_FILE"
        }, declarations.Select(m => m.Groups[1].Value));
        foreach (Match declaration in declarations)
        {
            var relative = declaration.Groups[2].Value;
            Assert.True(File.Exists(SafePaths.Within(result.ResourceDirectory, relative)));
            Assert.Contains("    '" + relative + "',", manifest);
            await using var stream = await package.Files.Single(f => f.DestinationPath == relative).Content.OpenReadAsync(default);
            using var original = new MemoryStream(); await stream.CopyToAsync(original);
            Assert.Equal(original.ToArray(), await File.ReadAllBytesAsync(SafePaths.Within(result.ResourceDirectory, relative)));
        }
    }
}
