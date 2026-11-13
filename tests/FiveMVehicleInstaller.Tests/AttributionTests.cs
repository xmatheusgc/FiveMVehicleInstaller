using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FiveMVehicleInstaller.Core;
using FiveMVehicleInstaller.Infrastructure;

namespace FiveMVehicleInstaller.Tests;

public sealed class AttributionTests
{
    [Theory]
    [InlineData("Converted to GTA5 by:Vsoreny", "Vsoreny", "Conversão / edição")]
    [InlineData("Converted by Jintei", "Jintei", "Conversão / edição")]
    [InlineData("Convert / edit . Just_Riko", "Just_Riko", "Conversão / edição")]
    [InlineData("conversion - CipherOG (me)", "CipherOG", "Conversão / edição")]
    [InlineData("Main Works: cereal", "cereal", "Trabalho principal")]
    [InlineData("Other Work: MiloJS", "MiloJS", "Trabalho principal")]
    [InlineData("Autor: João Silva", "João Silva", "Autor")]
    [InlineData("Created by: Alice & Bob", "Alice & Bob", "Autor")]
    [InlineData("**Author**: Alice", "Alice", "Autor")]
    [InlineData("1993 Nissan Cefiro A31 by Akkariin", "Akkariin", "Autoria no título")]
    public async Task ReadsExplicitCreditsFromReadmeOutsideDlc(string text, string author, string role)
    {
        using var w = new TestWorkspace();
        w.Write("input/car/dlc.rpf", RpfFixture.Create(PackageFixture.Files()));
        w.WriteText("input/READ ME PLEASE.txt", text);
        var converter = new VehicleConverter(temporaryParent: w.PathFor("temp"));
        using var analysis = await converter.AnalyzeAsync([w.PathFor("input")]);
        var credit = Assert.Single(Assert.Single(analysis.Packages).Credits);
        Assert.Equal(author, credit.Name); Assert.Equal(role, credit.Role);
        Assert.Equal(CreditConfidence.Explicit, credit.Confidence);
        Assert.Equal(text, credit.Evidence); Assert.Equal("READ ME PLEASE.txt", credit.SourcePath);
    }

    [Theory]
    [InlineData("Lexus IS300 By Navefa 1.2.rar", "Navefa")]
    [InlineData("Nissan Silvia S15 Spec-R by MiloJS.7z", "MiloJS")]
    [InlineData("Toyota Chaser by [CipherOG] v1.1.zip", "[CipherOG]")]
    public async Task FilenameIsOnlyAHint(string filename, string author)
    {
        using var w = new TestWorkspace();
        // Container contents use ZIP bytes; detection does not depend on its display extension.
        var archivePath = w.PathFor(filename);
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        { using var stream = archive.CreateEntry("car/dlc.rpf").Open(); stream.Write(RpfFixture.Create(PackageFixture.Files())); }
        var converter = new VehicleConverter(temporaryParent: w.PathFor("temp"));
        using var analysis = await converter.AnalyzeAsync([archivePath]);
        Assert.All(Assert.Single(analysis.Packages).Credits, c => { Assert.Equal(author, c.Name); Assert.Equal(CreditConfidence.NameHint, c.Confidence); });
        Assert.NotEmpty(analysis.Packages[0].Credits);
    }

    [Fact]
    public async Task DoesNotMixSiblingCarsOrTreatContributorsAndInstructionsAsCreators()
    {
        using var w = new TestWorkspace();
        PackageFixture.WriteFolder(w, "input/first", "first"); PackageFixture.WriteFolder(w, "input/second", "second");
        w.WriteText("input/first/readme.md", "Author: Alice\nHandling by: RE\nBase Model: Forza\nBlitz Shift Knob by Silver10211\nYou can spawn it by name car\nConvert the car using OpenIV\nhttps://example.com/users/Somebody");
        w.WriteText("input/second/readme.txt", "Converted by Bob");
        w.WriteText("input/readme.txt", "Author: WrongCollectionAuthor");
        var converter = new VehicleConverter(temporaryParent: w.PathFor("temp"));
        using var analysis = await converter.AnalyzeAsync([w.PathFor("input")]);
        Assert.Equal("Alice", Assert.Single(analysis.Packages.Single(p => p.Vehicles[0].ModelName == "first").Credits).Name);
        Assert.Equal("Bob", Assert.Single(analysis.Packages.Single(p => p.Vehicles[0].ModelName == "second").Credits).Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreservesEvidenceThroughSeparateExportPackUpdateAndSplit(bool unified)
    {
        using var w = new TestWorkspace();
        var input = PackageFixture.WriteFolder(w, "input"); w.WriteText("input/readme.md", "Converted by Alice");
        var converter = new VehicleConverter(temporaryParent: w.PathFor("temp"));
        using var analysis = await converter.AnalyzeAsync([input]); var original = Assert.Single(analysis.Packages);
        var export = unified ? await converter.ExportPackAsync([new(original, "car", new Dictionary<string, string>())], new("garage", w.PathFor("out"))) :
            await converter.ExportAsync(original, new("garage", w.PathFor("out"), new Dictionary<string, string>()));
        using (var index = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(export.ResourceDirectory, ".fvpack.json"))))
            Assert.Equal("Alice", index.RootElement.GetProperty("Packages")[0].GetProperty("Credits")[0].GetProperty("Name").GetString());
        var opened = await converter.OpenPackAsync(export.ResourceDirectory);
        using (opened.Session)
        {
            var restored = Assert.Single(opened.Session.Packages); Assert.Equal(original.Credits, restored.Credits);
            await converter.UpdatePackAsync(opened.Document, [new(restored, "renamed", new Dictionary<string, string> { [restored.Vehicles[0].Key] = "ADDER" })]);
        }
        using var updated = await converter.AnalyzeAsync([export.ResourceDirectory]);
        var package = Assert.Single(updated.Packages); Assert.Equal(original.Credits, package.Credits);
        var split = await converter.ExportAsync(package, new("split", w.PathFor("split"), new Dictionary<string, string>()));
        var report = await File.ReadAllTextAsync(Path.Combine(split.ResourceDirectory, "relatorio.txt"));
        Assert.Contains("Alice", report); Assert.Contains("Converted by Alice", report); Assert.Contains("readme.md", report);
    }

    [Fact]
    public async Task SupportsUnicodeReadmesAndIgnoresOversizedOptionalText()
    {
        using var w = new TestWorkspace(); var folder = PackageFixture.WriteFolder(w, "input");
        w.Write("input/README.md", Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("Autor: José")).ToArray());
        w.WriteText("input/huge.txt", "Author: Wrong\n" + new string('x', 256 * 1024));
        var converter = new VehicleConverter(temporaryParent: w.PathFor("temp"));
        using var analysis = await converter.AnalyzeAsync([folder]); Assert.Equal("José", Assert.Single(Assert.Single(analysis.Packages).Credits).Name);
    }

    [Fact]
    public async Task KeepsReadmesWithTheirArchiveAndExcludesFiveMCopyCredits()
    {
        using var w = new TestWorkspace();
        Directory.CreateDirectory(w.PathFor("inputs"));
        foreach (var name in new[] { "Alice", "Bob" })
        {
            using var archive = ZipFile.Open(w.PathFor("inputs/" + name + ".zip"), ZipArchiveMode.Create);
            using (var stream = archive.CreateEntry("addon/dlc.rpf").Open()) stream.Write(RpfFixture.Create(PackageFixture.Files(name.ToLowerInvariant())));
            using (var writer = new StreamWriter(archive.CreateEntry("readme.md").Open())) writer.Write("Author: " + name);
            using (var writer = new StreamWriter(archive.CreateEntry("FiveM/fxmanifest.lua").Open())) writer.Write("game 'gta5'");
            using (var writer = new StreamWriter(archive.CreateEntry("FiveM/readme.txt").Open())) writer.Write("Author: WrongConverter");
        }
        w.WriteText("inputs/cars-by-WrongCollectionAuthor.txt", "Author: WrongCollectionAuthor");
        var converter = new VehicleConverter(temporaryParent: w.PathFor("temporary"));
        // Keep the scanner's temporary workspace outside the imported folder.
        using var analysis = await converter.AnalyzeAsync([w.PathFor("inputs")]);
        Assert.Equal(2, analysis.Packages.Count);
        foreach (var package in analysis.Packages)
            Assert.Equal(package.Vehicles[0].ModelName, Assert.Single(package.Credits).Name.ToLowerInvariant());
    }
}

public sealed class LocalAttributionFactAttribute : FactAttribute
{
    public LocalAttributionFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIVEM_ATTRIBUTION_SAMPLES")))
            Skip = "Defina FIVEM_ATTRIBUTION_SAMPLES com pastas de mods separadas por ponto e vírgula.";
    }
}

public sealed class LocalAttributionTests
{
    [LocalAttributionFact]
    public async Task AuditsOriginalArchivesWithoutModifyingThem()
    {
        using var w = new TestWorkspace();
        var rows = new List<object>(); var count = 0;
        foreach (var directory in Environment.GetEnvironmentVariable("FIVEM_ATTRIBUTION_SAMPLES")!.Split(';'))
        foreach (var archive in Directory.EnumerateFiles(directory).Where(p => Path.GetExtension(p).ToLowerInvariant() is ".zip" or ".rar" or ".7z").Order(StringComparer.OrdinalIgnoreCase))
        {
            var before = new FileInfo(archive); var length = before.Length; var modified = before.LastWriteTimeUtc;
            var converter = new VehicleConverter(temporaryParent: w.PathFor("temp"));
            using var analysis = await converter.AnalyzeAsync([archive]);
            Assert.NotEmpty(analysis.Packages);
            foreach (var package in analysis.Packages)
            {
                if (Path.GetFileName(archive).StartsWith("04fa59")) Assert.Contains(package.Credits, c => c.Name == "Jintei");
                if (Path.GetFileName(archive).StartsWith("557e6e")) Assert.Contains(package.Credits, c => c.Name == "CipherOG");
                if (Path.GetFileName(archive).StartsWith("cb95bb")) Assert.Contains(package.Credits, c => c.Name == "Akkariin");
                if (directory.EndsWith("cars2", StringComparison.OrdinalIgnoreCase))
                    Assert.Contains(package.Credits, c => c.Name == "Vsoreny" && c.Confidence == CreditConfidence.Explicit);
            }
            var after = new FileInfo(archive); Assert.Equal(length, after.Length); Assert.Equal(modified, after.LastWriteTimeUtc);
            rows.Add(new { Archive = Path.GetFileName(archive), Diagnostics = analysis.Diagnostics, Packages = analysis.Packages.Select(p => new { p.SourceName, p.CanExport, Models = p.Vehicles.Select(v => v.ModelName), p.Credits }).ToArray() });
            count++;
        }
        Assert.True(count > 0);
        if (Environment.GetEnvironmentVariable("FIVEM_ATTRIBUTION_REPORT") is { Length: > 0 } report)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
