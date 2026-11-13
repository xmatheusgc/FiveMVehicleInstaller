using System.Security.Cryptography;
using FiveMVehicleInstaller.Core;
using FiveMVehicleInstaller.Infrastructure;

namespace FiveMVehicleInstaller.Tests;

public sealed class LocalChaserFactAttribute : FactAttribute
{
    public LocalChaserFactAttribute()
    {
        if (!File.Exists(Environment.GetEnvironmentVariable("FIVEM_CHASER_ARCHIVE")) ||
            !Directory.Exists(Environment.GetEnvironmentVariable("FIVEM_CHASER_REFERENCE")))
            Skip = "Defina FIVEM_CHASER_ARCHIVE e FIVEM_CHASER_REFERENCE para testar o Chaser local sem distribuí-lo.";
    }
}

public sealed class LocalChaserTests
{
    [LocalChaserFact]
    public async Task ChaserArchiveAndExistingResourceExportFlatWithAllAssetAndMetadataBytesPreserved()
    {
        using var workspace = new TestWorkspace();
        var reference = Environment.GetEnvironmentVariable("FIVEM_CHASER_REFERENCE")!;
        var originals = Directory.GetFiles(Path.Combine(reference,"stream"),"*",SearchOption.AllDirectories)
            .ToDictionary(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(54, originals.Count);
        var converter = new VehicleConverter(temporaryParent:workspace.PathFor("temp"));
        foreach (var input in new[] { Environment.GetEnvironmentVariable("FIVEM_CHASER_ARCHIVE")! })
        {
            using var analyzed = await converter.AnalyzeAsync([input]);
            Assert.DoesNotContain(analyzed.Diagnostics,d=>d.Severity==DiagnosticSeverity.Error);
            var package = Assert.Single(analyzed.Packages);
            Assert.True(package.CanExport,string.Join(";",package.Diagnostics.Select(d=>d.Message)));
            Assert.Equal("jzx100",Assert.Single(package.Vehicles).ModelName);
            var export = await converter.ExportAsync(package,new("chaser"+Guid.NewGuid().ToString("N"),workspace.PathFor("output"),new Dictionary<string,string>()));
            var streamDirectory=Path.Combine(export.ResourceDirectory,"stream");
            Assert.Empty(Directory.GetDirectories(streamDirectory));
            Assert.Equal(54,Directory.GetFiles(streamDirectory).Length);
            foreach (var file in package.Files)
            {
                var target=SafePaths.Within(export.ResourceDirectory,file.DestinationPath);
                await using var targetStream=File.OpenRead(target);
                await using var sourceStream=await file.Content.OpenReadAsync();
                var targetHash=await SHA256.HashDataAsync(targetStream);
                Assert.Equal(await SHA256.HashDataAsync(sourceStream),targetHash);
                if(file.DataFileType is null)
                {
                    await using var manual=File.OpenRead(originals[Path.GetFileName(file.DestinationPath)]);
                    Assert.Equal(await SHA256.HashDataAsync(manual),targetHash);
                }
            }
            Assert.Equal(5,package.Files.Count(f=>f.DataFileType is not null));
            using var imported=await converter.AnalyzeAsync([export.ResourceDirectory]);
            Assert.True(Assert.Single(imported.Packages).CanExport);
        }
    }
}