using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FiveMVehicleInstaller.Core;
using FiveMVehicleInstaller.Infrastructure;

namespace FiveMVehicleInstaller.Tests;

public sealed class FiveMReadyTests
{
    [Fact]
    public async Task IgnoresReadyVersionEvenWithMalformedXmlAndNeverExecutesManifest()
    {
        using var w=new TestWorkspace();
        w.WriteText("input/FiveM/car/fxmanifest.lua","error('must never execute')");
        w.WriteText("input/FiveM/car/data/vehicles.meta","<broken>");
        w.Write("input/FiveM/car/stream/car.yft",RpfFixture.Resource([9]));
        w.Write("input/Add-On/car/dlc.rpf",RpfFixture.Create(PackageFixture.Files()));
        var converter=new VehicleConverter(temporaryParent:w.PathFor("temp"));
        using var session=await converter.AnalyzeAsync([w.PathFor("input")]);
        var package=Assert.Single(session.Packages);Assert.True(package.CanExport);Assert.False(package.IsFromPack);
        Assert.DoesNotContain(package.Files,f=>f.SourcePath.Contains("FiveM"));
        Assert.DoesNotContain(session.Diagnostics,d=>d.Severity==DiagnosticSeverity.Error);
        Assert.Contains(session.Diagnostics,d=>d.Message.Contains("FiveM ready ignorada"));
        using var readyOnly=await converter.AnalyzeAsync([w.PathFor("input/FiveM/car")]);
        Assert.Empty(readyOnly.Packages);
    }
    [LocalCefiroFact]
    public async Task RealCefiroReadyAndAddonPreserveLayoutAndEveryExportedFile()
    {
        using var w = new TestWorkspace();
        var archive = Environment.GetEnvironmentVariable("FIVEM_CEFIRO_ARCHIVE")!;
        using var zip = ZipFile.OpenRead(archive);
        var converter = new VehicleConverter(temporaryParent:w.PathFor("temp"));
        using var session = await converter.AnalyzeAsync([archive]);
        Assert.Single(session.Packages);
        var ready=Assert.Single(session.Packages); Assert.False(ready.IsFromPack);
        Assert.All(session.Packages,p=>Assert.True(p.CanExport,string.Join(";",p.Diagnostics.Select(d=>d.Message))));
        Assert.Equal("nisa31",Assert.Single(ready.Vehicles).ModelName);
        Assert.Equal("elegy",ready.Vehicles[0].AudioNameHash);
        var entries=zip.Entries.Where(e=>e.FullName.StartsWith("FiveM/nisa31/stream/") && e.Length>0).ToDictionary(e=>Path.GetFileName(e.FullName),StringComparer.OrdinalIgnoreCase);
        Assert.True(entries.Count>200);
        foreach(var package in session.Packages)
        {
            var export=await converter.ExportAsync(package,new("addon",w.PathFor("out"),new Dictionary<string,string>()));
            Assert.Equal(entries.Count,package.Files.Count(f=>f.DataFileType is null));
            Assert.Equal(5,package.Files.Count(f=>f.DataFileType is not null));
            foreach(var file in package.Files)
            {
                var path=SafePaths.Within(export.ResourceDirectory,file.DestinationPath);
                await using var target=File.OpenRead(path);
                var actual=await SHA256.HashDataAsync(target);
                await using var source=await file.Content.OpenReadAsync();
                Assert.Equal(await SHA256.HashDataAsync(source),actual);
                var entry=file.DataFileType is null ? entries[Path.GetFileName(file.DestinationPath)] :
                    zip.GetEntry("FiveM/nisa31/common/"+Path.GetFileName(file.DestinationPath))!;
                await using var original=entry.Open();
                if (file.DataFileType is "VEHICLE_LAYOUTS_FILE" or "VEHICLE_METADATA_FILE")
                {
                    using var buffer = new MemoryStream(); await original.CopyToAsync(buffer);
                    var exportedBytes = await File.ReadAllBytesAsync(path);
                    var withoutIsolation = Regex.Replace(Encoding.UTF8.GetString(exportedBytes), @"FVLI_[0-9A-F]{16}_", "");
                    Assert.Equal(buffer.ToArray(), Encoding.UTF8.GetBytes(withoutIsolation));
                }
                else Assert.Equal(await SHA256.HashDataAsync(original),actual);
            }
            using var reimport=await converter.AnalyzeAsync([export.ResourceDirectory]);
            Assert.True(Assert.Single(reimport.Packages).CanExport);
        }
    }
}

public sealed class LocalCefiroFactAttribute : FactAttribute
{
    public LocalCefiroFactAttribute()
    {
        if(!File.Exists(Environment.GetEnvironmentVariable("FIVEM_CEFIRO_ARCHIVE")))
            Skip="Defina FIVEM_CEFIRO_ARCHIVE para validar o ZIP local sem distribuí-lo.";
    }
}