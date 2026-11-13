using System.IO;
using System.Windows.Controls;
using FiveMVehicleInstaller.App;
using FiveMVehicleInstaller.App.ViewModels;
using FiveMVehicleInstaller.Infrastructure;

namespace FiveMVehicleInstaller.Tests;

public sealed partial class WpfTests
{
    [Fact]
    public Task ShowsCreatorAndEvidenceInPackageDetails() => RunOnDispatcherAsync(async () =>
    {
        using var w = new TestWorkspace();
        var input = PackageFixture.WriteFolder(w, "input");
        w.WriteText("input/README.md", "Converted to GTA5 by:Vsoreny");
        var window = new MainWindow(new VehicleConverter(temporaryParent: w.PathFor("temp")), new FakeDialogs());
        try
        {
            var model = (MainViewModel)window.DataContext; model.AddInputs([input]); await model.AnalyzeCommand.ExecuteAsync(null);
            await RenderPreviewAsync(window, "interface-autoria.png", 1120, 800);
            var summary = (TextBlock)window.FindName("CreatorSummaryText"); Assert.Equal("Vsoreny", summary.Text);
            Assert.Contains("README.md", model.SelectedPackage!.CreditDetails);
            Assert.Contains("Converted to GTA5 by:Vsoreny", model.SelectedPackage.CreditDetails);
            ((TabItem)window.FindName("CreditsTab")).IsSelected = true;
            await RenderPreviewAsync(window, "interface-creditos.png", 1120, 800);
        }
        finally { window.Close(); }
    });
}
