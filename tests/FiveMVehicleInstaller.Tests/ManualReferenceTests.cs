using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FiveMVehicleInstaller.Infrastructure;

namespace FiveMVehicleInstaller.Tests;

public sealed class ManualReferenceFactAttribute : FactAttribute
{
    public ManualReferenceFactAttribute()
    {
        if (!Directory.Exists(Environment.GetEnvironmentVariable("FIVEM_VEHICLE_SAMPLE")) ||
            !Directory.Exists(Environment.GetEnvironmentVariable("FIVEM_MANUAL_VEHICLE_RESOURCE")))
            Skip = "Defina FIVEM_VEHICLE_SAMPLE e FIVEM_MANUAL_VEHICLE_RESOURCE para comparar com a extração manual local.";
    }
}

public sealed class ManualReferenceTests
{
    [ManualReferenceFact]
    public async Task MatchesEveryManualAssetPathAndByteIncludingSoundEditedMetadata()
    {
        using var workspace = new TestWorkspace();
        var reference = Environment.GetEnvironmentVariable("FIVEM_MANUAL_VEHICLE_RESOURCE")!;
        var sample = Path.Combine(Environment.GetEnvironmentVariable("FIVEM_VEHICLE_SAMPLE")!, "extracted");
        var manualVehicles = XDocument.Load(Path.Combine(reference, "data/vehicles.meta"));
        var manualSound = Assert.Single(manualVehicles.Descendants("audioNameHash")).Value.Trim();
        var converter = new VehicleConverter(temporaryParent: workspace.PathFor("temp"));
        using var analysis = await converter.AnalyzeAsync([sample]);
        var package = Assert.Single(analysis.Packages);
        Assert.True(package.CanExport, string.Join(";", package.Diagnostics.Select(d => d.Message)));
        var vehicle = Assert.Single(package.Vehicles);
        Assert.Equal("mk2100", vehicle.ModelName);
        var originalSource = await File.ReadAllBytesAsync(Path.Combine(sample, "data/vehicles.meta"));
        var result = await converter.ExportAsync(package, new("manual_comparison", workspace.PathFor("output"),
            new Dictionary<string, string> { [vehicle.Key] = manualSound }));
        var assets = package.Files.Where(f => f.DataFileType is null).ToArray();
        Assert.Equal(139, assets.Length);
        Assert.All(assets, f => Assert.Equal("stream/" + Path.GetFileName(f.DestinationPath), f.DestinationPath));
        Assert.Empty(Directory.GetDirectories(Path.Combine(result.ResourceDirectory, "stream")));
        foreach (var file in package.Files)
        {
            // The server reference has a manually customized handling; compare that file to its actual input.
            var expected = file.DataFileType == "HANDLING_FILE"
                ? SafePaths.Within(sample, file.DestinationPath)
                : file.DataFileType is null ? Directory.GetFiles(Path.Combine(reference, "stream"), Path.GetFileName(file.DestinationPath), SearchOption.AllDirectories).Single()
                : SafePaths.Within(reference, file.DestinationPath);
            var actual = SafePaths.Within(result.ResourceDirectory, file.DestinationPath);
            Assert.True(File.Exists(expected), "Arquivo ausente na referência manual: " + file.DestinationPath);
            var expectedHash = SHA256.HashData(await File.ReadAllBytesAsync(expected));
            var actualBytes = await File.ReadAllBytesAsync(actual);
            if (file.DataFileType is "VEHICLE_LAYOUTS_FILE" or "VEHICLE_METADATA_FILE")
                actualBytes = Encoding.UTF8.GetBytes(Regex.Replace(Encoding.UTF8.GetString(actualBytes), @"FVLI_[0-9A-F]{16}_", ""));
            var actualHash = SHA256.HashData(actualBytes);
            Assert.True(expectedHash.SequenceEqual(actualHash),
                "Conteúdo diferente: " + file.DestinationPath);
        }
        Assert.Equal(Directory.GetFiles(Path.Combine(reference, "stream"), "*", SearchOption.AllDirectories).Length, assets.Length);
        Assert.Equal(originalSource, await File.ReadAllBytesAsync(Path.Combine(sample, "data/vehicles.meta")));
        var manifest = await File.ReadAllTextAsync(Path.Combine(result.ResourceDirectory, "fxmanifest.lua"));
        Assert.Contains("data_file 'CARCOLS_FILE' 'data/carcols.meta'", manifest);
        Assert.Contains("data_file 'VEHICLE_VARIATION_FILE' 'data/carvariations.meta'", manifest);
    }
}
