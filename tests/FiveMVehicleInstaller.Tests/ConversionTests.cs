using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FiveMVehicleInstaller.Core;
using FiveMVehicleInstaller.Infrastructure;

namespace FiveMVehicleInstaller.Tests;

public sealed class ConversionTests
{
    [Theory]
    [InlineData("x64/levels/gta5/vehicles/jzx100vehicles.rpf", "x64/levels/jzx100/vehiclemods/jzx100_mods.rpf")]
    [InlineData("platform/textures/cars.rpf", "nested/tuning/parts.rpf")]
    public async Task FlattensUnusualLegacyDlcLayoutsLikeChaser(string modelsPath, string tuningPath)
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText("input/setup2.xml", "<SSetupData><nameHash>jzx100</nameHash></SSetupData>");
        var metas = new Dictionary<string,string>
        {
            ["common/data/levels/gta5/vehicles.meta"] = PackageFixture.Vehicles(("jzx100", "SULTAN")),
            ["common/data/handling.meta"] = "<CHandlingDataMgr/>",
            ["common/data/carcols.meta"] = "<CVehicleModelInfoVarGlobal/>",
            ["common/data/carvariations.meta"] = "<CVehicleModelInfoVariation/>"
        };
        foreach (var meta in metas) workspace.WriteText("input/" + meta.Key, meta.Value);
        workspace.Write("input/" + modelsPath, RpfFixture.Create(new FixtureFile("jzx100.yft", RpfFixture.Resource([1]), Resource:true),
            new FixtureFile("shared/jzx100.ytd", RpfFixture.Resource([2]), Resource:true)));
        workspace.Write("input/" + tuningPath, RpfFixture.Create(new FixtureFile("body/jzx100_bumper.yft", RpfFixture.Resource([3]), Resource:true)));
        var converter = new VehicleConverter(temporaryParent:workspace.PathFor("temp"));
        using var analysis = await converter.AnalyzeAsync([workspace.PathFor("input")]);
        var package = Assert.Single(analysis.Packages); Assert.True(package.CanExport);
        var exported = await converter.ExportAsync(package, new("chaser",workspace.PathFor("output"),new Dictionary<string,string>()));
        Assert.Empty(Directory.GetDirectories(Path.Combine(exported.ResourceDirectory,"stream")));
        Assert.Equal(3,Directory.GetFiles(Path.Combine(exported.ResourceDirectory,"stream")).Length);
        Assert.True(File.Exists(Path.Combine(exported.ResourceDirectory,"stream/jzx100_bumper.yft")));
        foreach (var meta in metas)
            Assert.Equal(Encoding.UTF8.GetBytes(meta.Value),await File.ReadAllBytesAsync(Path.Combine(exported.ResourceDirectory,"data",meta.Key)));
        using var reimported = await converter.AnalyzeAsync([exported.ResourceDirectory]);
        Assert.True(Assert.Single(reimported.Packages).CanExport);
    }
    [Fact]
    public async Task ExportsFolderWithSharedTexturesTuningAndAllManifestReferences()
    {
        using var workspace = new TestWorkspace();
        var input = PackageFixture.WriteFolder(workspace, "input");
        workspace.Write("input/x64/vehiclemods/tuning.rpf", RpfFixture.Create(new FixtureFile("car_bumper.yft", RpfFixture.Resource([7]), Resource: true)));
        workspace.Write("input/shared/interior.ytd", RpfFixture.Resource([8]));
        workspace.WriteText("input/data/ai/layouts.meta", "<CVehicleMetadataMgr/>");
        var converter = new VehicleConverter(temporaryParent: workspace.PathFor("temp"));
        using var analysis = await converter.AnalyzeAsync([input]);
        var package = Assert.Single(analysis.Packages); Assert.True(package.CanExport);
        Assert.Equal("SULTAN", Assert.Single(package.Vehicles).AudioNameHash);
        var exported = await converter.ExportAsync(package, new("car", workspace.PathFor("output"), new Dictionary<string, string>()));
        Assert.Equal("ensure car", exported.EnsureCommand);
        Assert.Contains(package.Files, f => f.DestinationPath == "stream/car_bumper.yft");
        Assert.Contains(package.Files, f => f.DestinationPath == "stream/car.yft");
        Assert.Contains(package.Files, f => f.DestinationPath.EndsWith("interior.ytd"));
        var manifest = await File.ReadAllTextAsync(Path.Combine(exported.ResourceDirectory, "fxmanifest.lua"));
        Assert.Contains("VEHICLE_LAYOUTS_FILE", manifest); Assert.Contains("fx_version 'cerulean'", manifest);
        foreach (var file in package.Files)
        {
            Assert.True(File.Exists(SafePaths.Within(exported.ResourceDirectory, file.DestinationPath)));
            if (file.DataFileType is not null) Assert.Contains($"data_file '{file.DataFileType}' '{file.DestinationPath}'", manifest);
        }
        Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(input, "data/vehicles.meta")),
            await File.ReadAllBytesAsync(Path.Combine(exported.ResourceDirectory, "data/vehicles.meta")));
    }

    [Fact]
    public async Task FlattensVehicleTuningAndExistingStreamFoldersWithoutChangingAssets()
    {
        using var workspace = new TestWorkspace(); var input = PackageFixture.WriteFolder(workspace, "input");
        workspace.Write("input/x64/vehicles.rpf", RpfFixture.Create(
            new FixtureFile("cars/car.yft", RpfFixture.Resource([1]), Resource: true),
            new FixtureFile("shared/interior.ytd", RpfFixture.Resource([2]), Resource: true)));
        workspace.Write("input/x64/vehiclemods/car_mods.rpf", RpfFixture.Create(
            new FixtureFile("bumpers/car_bumper.yft", RpfFixture.Resource([3]), Resource: true)));
        workspace.Write("input/stream/custom/extra.ytd", RpfFixture.Resource([4]));
        var converter = new VehicleConverter(temporaryParent: workspace.PathFor("temp"));
        using var analysis = await converter.AnalyzeAsync([input]); var package = Assert.Single(analysis.Packages);
        Assert.True(package.CanExport);
        Assert.Contains(package.Files, f => f.DestinationPath == "stream/car.yft");
        Assert.Contains(package.Files, f => f.DestinationPath == "stream/interior.ytd");
        Assert.Contains(package.Files, f => f.DestinationPath == "stream/car_bumper.yft");
        Assert.Contains(package.Files, f => f.DestinationPath == "stream/extra.ytd");
        var exported = await converter.ExportAsync(package, new("flat", workspace.PathFor("output"), new Dictionary<string, string>()));
        Assert.Empty(Directory.GetDirectories(Path.Combine(exported.ResourceDirectory, "stream")));
        Assert.Equal(RpfFixture.Resource([1]), await File.ReadAllBytesAsync(Path.Combine(exported.ResourceDirectory, "stream/car.yft")));
        Assert.Equal(RpfFixture.Resource([2]), await File.ReadAllBytesAsync(Path.Combine(exported.ResourceDirectory, "stream/interior.ytd")));
        Assert.Equal(RpfFixture.Resource([3]), await File.ReadAllBytesAsync(Path.Combine(exported.ResourceDirectory, "stream/car_bumper.yft")));
        Assert.Equal(RpfFixture.Resource([4]), await File.ReadAllBytesAsync(Path.Combine(exported.ResourceDirectory, "stream/extra.ytd")));
    }
    [Fact]
    public async Task ChangesOnlySelectedVehicleSoundAndLeavesSourceUntouched()
    {
        using var workspace = new TestWorkspace(); var input = PackageFixture.WriteFolder(workspace, "input");
        var original = PackageFixture.Vehicles(("car", "SULTAN"), ("other", "ADDER"));
        var source = workspace.WriteText("input/data/vehicles.meta", original);
        workspace.Write("input/other.yft", RpfFixture.Resource([9]));
        var converter = new VehicleConverter(temporaryParent: workspace.PathFor("temp"));
        using var analysis = await converter.AnalyzeAsync([input]); var package = Assert.Single(analysis.Packages);
        var exported = await converter.ExportAsync(package, new("sounds", workspace.PathFor("output"),
            new Dictionary<string, string> { [package.Vehicles[0].Key] = "CUSTOM_ENGINE" }));
        var changed = XDocument.Load(Path.Combine(exported.ResourceDirectory, "data/vehicles.meta"));
        var expected = XDocument.Parse(original); expected.Descendants("audioNameHash").First().Value = "CUSTOM_ENGINE";
        Assert.True(XNode.DeepEquals(expected, changed));
        Assert.Equal(original, await File.ReadAllTextAsync(source));
    }

    [Fact]
    public async Task FindsMultipleDlcsInZipAndIgnoresExternalHandlingAlternative()
    {
        using var workspace = new TestWorkspace();
        var archivePath = workspace.PathFor("cars.zip");
        using (var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            foreach (var model in new[] { "car", "other" })
            {
                var entry = zip.CreateEntry("unusual/folder/" + model + "/dlc.rpf");
                using var stream = entry.Open(); stream.Write(RpfFixture.Create(PackageFixture.Files(model)));
            }
            using var alternative = new StreamWriter(zip.CreateEntry("Drift Handling/handling.meta").Open());
            alternative.Write("<CHandlingDataMgr><alternative/></CHandlingDataMgr>");
        }
        var converter = new VehicleConverter(temporaryParent: workspace.PathFor("temp"));
        using var analysis = await converter.AnalyzeAsync([archivePath]);
        Assert.Equal(2, analysis.Packages.Count);
        Assert.All(analysis.Packages, p => Assert.True(p.CanExport, string.Join(";", p.Diagnostics.Select(d => d.Message))));
        Assert.All(analysis.Packages, p => Assert.DoesNotContain(p.Files, f => f.SourcePath.Contains("Drift Handling")));
    }

    [Fact]
    public async Task RecognizesCustomMetadataFilenameWithoutSetupOrContent()
    {
        using var workspace = new TestWorkspace();
        workspace.Write("input/car.rpf", RpfFixture.Create(new FixtureFile("anything/configuration.meta", Encoding.UTF8.GetBytes(PackageFixture.Vehicles(("car", "SULTAN")))), new FixtureFile("anything/car.yft", RpfFixture.Resource([1]), Resource:true)));
        var converter = new VehicleConverter(temporaryParent: workspace.PathFor("temp"));
        using var analysis = await converter.AnalyzeAsync([workspace.PathFor("input")]);
        Assert.True(Assert.Single(analysis.Packages).CanExport);
    }

    [Fact]
    public async Task ConvertsSevenZipWithSequentialExtraction()
    {
        // Generated from synthetic vehicles.meta + car.yft with Windows bsdtar (7zip).
        const string fixture = "N3q8ryccAAPCx2vA8wEAAAAAAABoAAAAAAAAABesv2gAG5GGBTb3owo3j+cG5hACXJH45OsDuG6JCSDJ7ZG2KBco3D+TQcDZB7G9IW1emKnoK8DSlbOb5Z+EeJ6jHGXzPlV/OjgGUtJ9Cf+p54SK/wOE7qjjY81lrRhvgZ9NcMn+ekAlvUYlg8p5TRdKXaj9Wm9flt+VCGHUCNzhyp07h+BnFgT6oY9UEiUqtdTzR6LIuP8a/l+4qeAxeV9B4V1fznffxyFaLocaB5Tbt107KEugOkS3q90EQL5jeJdqKBLnkwDRkxdfXV75SzfmbqugrMeIiw4jJ8q+Hmg2zXsASJ/ttHKrRHKinpTMEcT8BkKtm8sGHb04h5oWiySAx94I18ndxvNp3z/1ZYEm+4odrefichXYUQExOKes+zGoNu4WFvF5KiDlL9AMHJu+9UHHuYFQ/4toJmUHvfKrcAxmuYHmRRjKhBCciIYGvT5fSmzziaeRH9sInL+fwLsaIkkOWm8nzWcd32f/gBOu63onHRhucnIy7/HWpoRMc2nKHA6ouA7tSzwYGEzuJvjXFS14V/+K105LqIupVZUSpH+XREivebWpJd1zo+9jvLetbqbFYk6JVLhyfYKHrCLxjAzAOBF6YVF3PZz1IFWciPNZ28amcc+sM3HJ1gzWdnYHefoc9Ok1QcR6ND9favzW+f/7kQVeAQQGAAEJgfMABwsBAAEjAwEBBV0AAIAADJAAAAgKAbbQBDkAAAUBEREAZABsAGMALgByAHAAZgAAABQKAQBEzzs3iVvdARIKAQAcqDs3iVvdARMKAQBEzzs3iVvdARUGAQAggLaBAAA=";
        using var workspace = new TestWorkspace(); var input = workspace.Write("cars.7z", Convert.FromBase64String(fixture));
        var converter = new VehicleConverter(temporaryParent: workspace.PathFor("temp"));
        using var analysis = await converter.AnalyzeAsync([input]);
        Assert.DoesNotContain(analysis.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var package = Assert.Single(analysis.Packages); Assert.True(package.CanExport);
        Assert.Equal("car", Assert.Single(package.Vehicles).ModelName);
        var result = await converter.ExportAsync(package, new("sevenzip", workspace.PathFor("output"), new Dictionary<string, string>()));
        Assert.Equal(20, new FileInfo(Path.Combine(result.ResourceDirectory, "stream/car.yft")).Length);
    }

    [Theory]
    [InlineData("xml")][InlineData("model")][InlineData("duplicate")][InlineData("encrypted")]
    public async Task BlocksIncompleteOrConflictingPackages(string failure)
    {
        using var workspace = new TestWorkspace(); var input = PackageFixture.WriteFolder(workspace, "input");
        switch (failure)
        {
            case "xml": workspace.WriteText("input/data/vehicles.meta", "<broken>"); break;
            case "model": workspace.WriteText("input/data/vehicles.meta", PackageFixture.Vehicles(("missing", "SULTAN"))); break;
            case "duplicate": workspace.Write("input/other/car.yft", RpfFixture.Resource([2])); break;
            case "encrypted":
                var encrypted = RpfFixture.Create(new FixtureFile("x", [1]));
                BinaryPrimitives.WriteUInt32LittleEndian(encrypted.AsSpan(12), 0x0FFFFFF9);
                workspace.Write("input/x64/encrypted.rpf", encrypted); break;
        }
        var converter = new VehicleConverter(temporaryParent: workspace.PathFor("temp"));
        using var analysis = await converter.AnalyzeAsync([input]); var package = Assert.Single(analysis.Packages);
        Assert.False(package.CanExport); Assert.Contains(package.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        await Assert.ThrowsAsync<InvalidOperationException>(() => converter.ExportAsync(package,
            new("blocked", workspace.PathFor("output"), new Dictionary<string, string>())));
    }

    [Fact]
    public async Task RejectsZipTraversalAndKeepsIndependentInputsWorking()
    {
        using var workspace = new TestWorkspace(); var valid = PackageFixture.WriteFolder(workspace, "valid");
        var zipPath = workspace.PathFor("malicious.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        { using var stream = zip.CreateEntry("../escape.txt").Open(); stream.Write([1]); }
        var converter = new VehicleConverter(temporaryParent: workspace.PathFor("temp"));
        using var analysis = await converter.AnalyzeAsync([zipPath, valid]);
        Assert.Contains(analysis.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.True(Assert.Single(analysis.Packages).CanExport);
        Assert.False(File.Exists(workspace.PathFor("escape.txt")));
    }

    [Fact]
    public async Task NeverOverwritesExistingResource()
    {
        using var workspace = new TestWorkspace(); var input = PackageFixture.WriteFolder(workspace, "input");
        workspace.WriteText("output/car/sentinel.txt", "keep");
        var converter = new VehicleConverter(temporaryParent: workspace.PathFor("temp"));
        using var analysis = await converter.AnalyzeAsync([input]);
        await Assert.ThrowsAsync<IOException>(() => converter.ExportAsync(Assert.Single(analysis.Packages),
            new("car", workspace.PathFor("output"), new Dictionary<string, string>())));
        Assert.Equal("keep", await File.ReadAllTextAsync(workspace.PathFor("output/car/sentinel.txt")));
    }

    [Fact]
    public async Task CancellationRemovesStagingAndDoesNotPublishPartialResource()
    {
        using var workspace = new TestWorkspace(); var input = PackageFixture.WriteFolder(workspace, "input");
        var converter = new VehicleConverter(temporaryParent: workspace.PathFor("temp"));
        using var analysis = await converter.AnalyzeAsync([input]); using var cancellation = new CancellationTokenSource();
        var progress = new ImmediateProgress(p => { if (p.Completed > 0) cancellation.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => converter.ExportAsync(Assert.Single(analysis.Packages),
            new("cancelled", workspace.PathFor("output"), new Dictionary<string, string>()), progress, cancellation.Token));
        Assert.Empty(Directory.GetFileSystemEntries(workspace.PathFor("output")));
    }

    [Fact]
    public async Task DetectsSourceChangesAfterAnalysisAndCleansFailedExport()
    {
        using var workspace = new TestWorkspace(); var input = PackageFixture.WriteFolder(workspace, "input");
        var converter = new VehicleConverter(temporaryParent: workspace.PathFor("temp"));
        using var analysis = await converter.AnalyzeAsync([input]);
        workspace.WriteText("input/data/vehicles.meta", "<modified/>");
        await Assert.ThrowsAsync<IOException>(() => converter.ExportAsync(Assert.Single(analysis.Packages),
            new("changed", workspace.PathFor("output"), new Dictionary<string, string>())));
        Assert.Empty(Directory.GetFileSystemEntries(workspace.PathFor("output")));
    }

    [Fact]
    public async Task BlocksMissingActiveContentReference()
    {
        using var workspace = new TestWorkspace(); var input = PackageFixture.WriteFolder(workspace, "input");
        workspace.WriteText("input/content.xml", "<CDataFileMgr__ContentsOfDataFileXml><dataFiles><Item><filename>dlc_test:/data/missing.meta</filename><fileType>VEHICLE_LAYOUTS_FILE</fileType></Item></dataFiles></CDataFileMgr__ContentsOfDataFileXml>");
        var converter = new VehicleConverter(temporaryParent: workspace.PathFor("temp"));
        using var analysis = await converter.AnalyzeAsync([input]);
        var package = Assert.Single(analysis.Packages); Assert.False(package.CanExport);
        Assert.Contains(package.Diagnostics, d => d.Message.Contains("missing.meta"));
    }

    [Fact]
    public async Task InvalidArchiveAfterValidDlcDoesNotLeavePartialPackage()
    {
        using var workspace = new TestWorkspace(); var archivePath = workspace.PathFor("partial.zip");
        using (var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            using (var stream = zip.CreateEntry("car/dlc.rpf").Open()) stream.Write(RpfFixture.Create(PackageFixture.Files()));
            using (var stream = zip.CreateEntry("../escape").Open()) stream.Write([1]);
        }
        var converter = new VehicleConverter(temporaryParent: workspace.PathFor("temp"));
        using var analysis = await converter.AnalyzeAsync([archivePath]);
        Assert.Empty(analysis.Packages); Assert.Contains(analysis.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task CancelledAnalysisCleansWorkspace()
    {
        using var workspace = new TestWorkspace(); var input = PackageFixture.WriteFolder(workspace, "input");
        var converter = new VehicleConverter(temporaryParent: workspace.PathFor("temp"));
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => converter.AnalyzeAsync([input],
            new ImmediateProgress(_ => cancellation.Cancel()), cancellation.Token));
        Assert.Empty(Directory.GetFileSystemEntries(workspace.PathFor("temp")));
    }

    private sealed class ImmediateProgress(Action<ConversionProgress> report) : IProgress<ConversionProgress>
    { public void Report(ConversionProgress value) => report(value); }
}
