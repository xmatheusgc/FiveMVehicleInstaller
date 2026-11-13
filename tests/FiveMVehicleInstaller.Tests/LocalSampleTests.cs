using System.Security.Cryptography;
using System.Xml.Linq;
using FiveMVehicleInstaller.Infrastructure;

namespace FiveMVehicleInstaller.Tests;

public sealed class LocalSampleFactAttribute : FactAttribute
{
    public LocalSampleFactAttribute()
    {
        if (!Directory.Exists(Environment.GetEnvironmentVariable("FIVEM_VEHICLE_SAMPLE")))
            Skip = "Defina FIVEM_VEHICLE_SAMPLE para testar um mod local, sem incluí-lo no repositório.";
    }
}

public sealed class LocalSampleRarFactAttribute : FactAttribute
{
    public LocalSampleRarFactAttribute()
    {
        var sample = Environment.GetEnvironmentVariable("FIVEM_VEHICLE_SAMPLE");
        var archive = Environment.GetEnvironmentVariable("FIVEM_VEHICLE_SAMPLE_ARCHIVE");
        if (!Directory.Exists(sample) || (string.IsNullOrEmpty(archive) ? Directory.GetFiles(sample, "*.rar").Length != 1 : !File.Exists(archive)))
            Skip = "O teste pasta/RAR requer a extração e um RAR na raiz da amostra ou em FIVEM_VEHICLE_SAMPLE_ARCHIVE.";
    }
}
public sealed class LocalSampleTests
{
    [LocalSampleFact]
    public async Task RealVehicleSurvivesPackRoundTripWithAllMetadataAndTuningBytesIntact()
    {
        using var workspace = new TestWorkspace();
        var input = Path.Combine(Environment.GetEnvironmentVariable("FIVEM_VEHICLE_SAMPLE")!, "extracted");
        var other = PackageFixture.WriteFolder(workspace, "other", "roundtrip_other");
        var converter = new VehicleConverter(temporaryParent: workspace.PathFor("temp"));
        using var analysis = await converter.AnalyzeAsync([input, other]);
        Assert.Equal(2, analysis.Packages.Count);
        var original = Assert.Single(analysis.Packages, p => p.Vehicles.Any(v => v.ModelName == "mk2100"));
        var pack = await converter.ExportPackAsync(analysis.Packages.Select(p => new Core.PackageExportRequest(p,
            p.Vehicles[0].ModelName, new Dictionary<string, string>())).ToArray(), new("real_pack", workspace.PathFor("pack")));
        using var recovered = await converter.AnalyzeAsync([pack.ResourceDirectory]);
        Assert.Equal(2, recovered.Packages.Count);
        var vehiclePackage = Assert.Single(recovered.Packages, p => p.Vehicles.Any(v => v.ModelName == "mk2100"));
        Assert.True(vehiclePackage.CanExport, string.Join(";", vehiclePackage.Diagnostics.Select(d => d.Message)));
        Assert.Equal("SULTAN", Assert.Single(vehiclePackage.Vehicles).AudioNameHash);
        var restored = await converter.ExportAsync(vehiclePackage, new("mk2100_restored", workspace.PathFor("restored"), new Dictionary<string, string>()));
        Assert.Equal(original.Files.Count, vehiclePackage.Files.Count);
        foreach (var file in original.Files)
        {
            await using var originalStream = await file.Content.OpenReadAsync();
            await using var actualStream = File.OpenRead(SafePaths.Within(restored.ResourceDirectory, file.DestinationPath));
            Assert.Equal(await SHA256.HashDataAsync(originalStream), await SHA256.HashDataAsync(actualStream));
        }
        Assert.Empty(Directory.GetDirectories(Path.Combine(restored.ResourceDirectory, "stream")));
        Assert.Equal(139, Directory.GetFiles(Path.Combine(restored.ResourceDirectory, "stream")).Length);
        Assert.Contains(vehiclePackage.Files, f => f.DestinationPath.EndsWith("vehicles_mk2100_interior.ytd"));
        Assert.Equal(5, vehiclePackage.Files.Count(f => f.DataFileType is not null));
    }
    [LocalSampleRarFact]
    public async Task ConvertsRealFolderAndRarPreservingModelsTuningAndDefaultHandling()
    {
        using var workspace = new TestWorkspace();
        var sample = Environment.GetEnvironmentVariable("FIVEM_VEHICLE_SAMPLE")!;
        var folder = Path.Combine(sample, "extracted");
        var rar = Environment.GetEnvironmentVariable("FIVEM_VEHICLE_SAMPLE_ARCHIVE");
        if (string.IsNullOrEmpty(rar)) rar = Assert.Single(Directory.GetFiles(sample, "*.rar"));
        var originalMetadata = await File.ReadAllBytesAsync(Path.Combine(folder, "data/vehicles.meta"));
        var originalHandling = await File.ReadAllBytesAsync(Path.Combine(folder, "data/handling.meta"));
        var converter = new VehicleConverter(temporaryParent: workspace.PathFor("temp"));
        string? folderExport = null;
        foreach (var (input, name) in new[] { (folder, "mk2100_folder"), (rar, "mk2100_rar") })
        {
            using var analysis = await converter.AnalyzeAsync([input]);
            Assert.DoesNotContain(analysis.Diagnostics, d => d.Severity == Core.DiagnosticSeverity.Error);
            var package = Assert.Single(analysis.Packages);
            Assert.True(package.CanExport, string.Join("; ", package.Diagnostics.Select(d => d.Message)));
            var vehicle = Assert.Single(package.Vehicles); Assert.Equal("mk2100", vehicle.ModelName); Assert.Equal("SULTAN", vehicle.AudioNameHash);
            Assert.DoesNotContain(package.Files, f => f.SourcePath.Contains("Drift Handling"));
            Assert.Contains(package.Files, f => f.DestinationPath.EndsWith("vehicles_mk2100_interior.ytd"));
            Assert.Contains(package.Files, f => f.DataFileType == "VEHICLE_LAYOUTS_FILE");
            Assert.True(package.Files.Count(f => f.DataFileType is null) > 100);
            var result = await converter.ExportAsync(package, new(name, workspace.PathFor("output"), new Dictionary<string, string>()));
            Assert.Equal(originalMetadata, await File.ReadAllBytesAsync(Path.Combine(result.ResourceDirectory, "data/vehicles.meta")));
            Assert.Equal(originalHandling, await File.ReadAllBytesAsync(Path.Combine(result.ResourceDirectory, "data/handling.meta")));
            var model = Assert.Single(Directory.GetFiles(result.ResourceDirectory, "mk2100.yft", SearchOption.AllDirectories));
            await using (var stream = File.OpenRead(model))
            {
                var header = new byte[16]; await stream.ReadExactlyAsync(header);
                Assert.Equal("RSC7", System.Text.Encoding.ASCII.GetString(header, 0, 4));
                await using var deflate = new System.IO.Compression.DeflateStream(stream, System.IO.Compression.CompressionMode.Decompress);
                await deflate.CopyToAsync(Stream.Null);
            }
            if (folderExport is null) folderExport = result.ResourceDirectory;
            else
            {
                foreach (var file in package.Files)
                    Assert.Equal(SHA256.HashData(await File.ReadAllBytesAsync(SafePaths.Within(folderExport, file.DestinationPath))),
                        SHA256.HashData(await File.ReadAllBytesAsync(SafePaths.Within(result.ResourceDirectory, file.DestinationPath))));
            }
        }
        using var editedAnalysis = await converter.AnalyzeAsync([folder]); var editedPackage = Assert.Single(editedAnalysis.Packages);
        var edited = await converter.ExportAsync(editedPackage, new("mk2100_sound", workspace.PathFor("output"),
            new Dictionary<string, string> { [editedPackage.Vehicles[0].Key] = "ADDER" }));
        Assert.Equal("ADDER", XDocument.Load(Path.Combine(edited.ResourceDirectory, "data/vehicles.meta")).Descendants("audioNameHash").Single().Value);
        Assert.Equal(originalMetadata, await File.ReadAllBytesAsync(Path.Combine(folder, "data/vehicles.meta")));
    }
}
