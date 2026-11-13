using System.ComponentModel;
using System.IO;
using System.Windows.Controls.Primitives;
using FiveMVehicleInstaller.Core;
using FiveMVehicleInstaller.Infrastructure;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FiveMVehicleInstaller.App;
using FiveMVehicleInstaller.App.ViewModels;

namespace FiveMVehicleInstaller.Tests;

public sealed partial class WpfTests
{
    [Fact]
    public Task AnalyzesEditsSoundAndExportsThroughWpfCommands() => RunOnDispatcherAsync(async () =>
    {
        using var workspace = new TestWorkspace();
        var folder = PackageFixture.WriteFolder(workspace, "input");
        var window = new MainWindow(new VehicleConverter(), null);
        try
        {
            var model = Assert.IsType<MainViewModel>(window.DataContext);
            model.AddInputs([folder]);
            Assert.True(model.AnalyzeCommand.CanExecute(null));
            var analyze = model.AnalyzeCommand.ExecuteAsync(null);
            Assert.True(model.IsBusy); Assert.False(model.AddFilesCommand.CanExecute(null));
            await analyze;
            Assert.False(model.IsBusy);
            Assert.Single(model.Packages); Assert.NotNull(model.SelectedPackage);
            model.SelectedPackage.ResourceName = "car_ui";
            model.DestinationDirectory = workspace.PathFor("output");
            var content = (FrameworkElement)window.Content;
            await RenderPreviewAsync(window, "interface.png", 1360, 880);
            await Dispatcher.Yield(DispatcherPriority.Background);            var soundField = Descendants(content).OfType<TextBox>().Single(box =>
                BindingOperations.GetBindingExpression(box, TextBox.TextProperty)?.ParentBinding.Path.Path == "AudioNameHash");
            Assert.Equal("SULTAN", soundField.Text);
            soundField.Text = "ADDER";
            soundField.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Assert.Equal("ADDER", model.SelectedPackage.Vehicles[0].AudioNameHash);
            content.UpdateLayout();
            await RenderPreviewAsync(window, "interface-compacta.png", 1120, 800);
            await model.ExportCommand.ExecuteAsync(null);
            Assert.Contains("ensure car_ui", model.Messages);
            Assert.True(File.Exists(workspace.PathFor("output/car_ui/fxmanifest.lua")));
            Assert.Contains("ADDER", await File.ReadAllTextAsync(workspace.PathFor("output/car_ui/data/vehicles.meta")));
            model.ClearCommand.Execute(null); Assert.Empty(model.Packages); Assert.Empty(model.Inputs);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task RoutesEachPackageToItsChosenFolderWithoutRequiringGlobalDestination() => RunOnDispatcherAsync(async () =>
    {
        using var workspace = new TestWorkspace();
        var first = PackageFixture.WriteFolder(workspace, "first"); var second = PackageFixture.WriteFolder(workspace, "second", "other");
        var dialogs = new FakeDialogs(); dialogs.Folders.Enqueue(workspace.PathFor("destination_a")); dialogs.Folders.Enqueue(workspace.PathFor("destination_b"));
        var window = new MainWindow(new VehicleConverter(temporaryParent: workspace.PathFor("temp")), dialogs);
        try
        {
            var model = (MainViewModel)window.DataContext;
            await RenderPreviewAsync(window, "interface-vazia.png", 1360, 880);
            model.AddInputs([first, second]); await model.AnalyzeCommand.ExecuteAsync(null);
            Assert.Equal(2, model.Packages.Count); Assert.False(model.HasPendingInputs);
            model.SelectedPackage = model.Packages[0]; model.PickPackageDestinationCommand.Execute(model.Packages[0]);
            model.SelectedPackage = model.Packages[1]; model.PickPackageDestinationCommand.Execute(model.Packages[1]);
            Assert.Equal("", model.DestinationDirectory);
            model.SelectNoneCommand.Execute(null); Assert.False(model.ExportCommand.CanExecute(null));
            model.SelectAllCommand.Execute(null); Assert.True(model.ExportCommand.CanExecute(null));
            await model.ExportCommand.ExecuteAsync(null);
            Assert.True(File.Exists(workspace.PathFor("destination_a/car/fxmanifest.lua")));
            Assert.True(File.Exists(workspace.PathFor("destination_b/other/fxmanifest.lua")));
            Assert.Equal(2, dialogs.FolderRequests); Assert.True(model.CopyEnsureCommand.CanExecute(null));
            model.UseDefaultDestinationCommand.Execute(model.Packages[1]); Assert.Equal("", model.Packages[1].DestinationDirectory);
            model.SearchText = "other"; Assert.Single(model.PackagesView.Cast<object>());
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task SwitchesModesThroughRadioBindingsAndRecoversPackForSeparateExport() => RunOnDispatcherAsync(async () =>
    {
        using var workspace = new TestWorkspace();
        var first = PackageFixture.WriteFolder(workspace, "first"); var second = PackageFixture.WriteFolder(workspace, "second", "other");
        var window = new MainWindow(new VehicleConverter(temporaryParent: workspace.PathFor("temp")), new FakeDialogs());
        try
        {
            var model = (MainViewModel)window.DataContext;
            model.AddInputs([first, second]); await model.AnalyzeCommand.ExecuteAsync(null);
            model.DestinationDirectory = workspace.PathFor("packs"); model.PackResourceName = "garage";
            model.Packages[0].DestinationDirectory = workspace.PathFor("ignored_a"); model.Packages[1].DestinationDirectory = workspace.PathFor("ignored_b");
            var radio = Descendants((FrameworkElement)window.Content).OfType<RadioButton>().Single(r =>
                BindingOperations.GetBindingExpression(r, ToggleButton.IsCheckedProperty)?.ParentBinding.Path.Path == "ExportAsPack");
            radio.IsChecked = true; radio.GetBindingExpression(ToggleButton.IsCheckedProperty)!.UpdateSource();
            Assert.True(model.ExportAsPack); Assert.False(model.ExportSeparately);
            await RenderPreviewAsync(window, "interface-pack.png", 1360, 880);
            await model.ExportCommand.ExecuteAsync(null);
            Assert.True(File.Exists(workspace.PathFor("packs/garage/.fvpack.json")));
            Assert.False(Directory.Exists(workspace.PathFor("ignored_a"))); Assert.False(Directory.Exists(workspace.PathFor("ignored_b")));
            model.ClearCommand.Execute(null); model.AddInputs([workspace.PathFor("packs/garage")]);
            await model.AnalyzeCommand.ExecuteAsync(null); Assert.Equal(2, model.Packages.Count);
            var separate = Descendants((FrameworkElement)window.Content).OfType<RadioButton>().Single(r =>
                BindingOperations.GetBindingExpression(r, ToggleButton.IsCheckedProperty)?.ParentBinding.Path.Path == "ExportSeparately");
            separate.IsChecked = true; separate.GetBindingExpression(ToggleButton.IsCheckedProperty)!.UpdateSource();
            Assert.False(model.ExportAsPack); Assert.True(model.ExportSeparately);
            model.DestinationDirectory = workspace.PathFor("separated");
            await model.ExportCommand.ExecuteAsync(null);
            Assert.True(File.Exists(workspace.PathFor("separated/car/fxmanifest.lua")));
            Assert.True(File.Exists(workspace.PathFor("separated/other/fxmanifest.lua")));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ShowsImportErrorsEvenWhenNoPackageCanBeDiscovered() => RunOnDispatcherAsync(async () =>
    {
        using var workspace = new TestWorkspace(); workspace.WriteText("bad/.fvpack.json", "{invalid");
        var window = new MainWindow(new VehicleConverter(temporaryParent: workspace.PathFor("temp")), new FakeDialogs());
        try
        {
            var model = (MainViewModel)window.DataContext; model.AddInputs([workspace.PathFor("bad")]);
            await model.AnalyzeCommand.ExecuteAsync(null);
            Assert.Empty(model.Packages); Assert.True(model.ShowActivity); Assert.Contains(model.Messages, m => m.StartsWith("Erro:"));
            await RenderPreviewAsync(window, "interface-erros.png", 1360, 880);
            model.ToggleActivityCommand.Execute(null); Assert.False(model.ShowActivity);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ExportsScreenshotNamesThroughBindingsAndPreservesMetadataAndModels() => RunOnDispatcherAsync(async () =>
    {
        using var workspace = new TestWorkspace();
        string[] names = ["Nissan Silvia Ks 1998 S14 Kouki", "Toyota Chaser JZX100", "Nissan Silvia S13", "Nissan 180SX Type X", "Nissan-Silvia-S15-Spec-R"];
        var inputs = names.Select((_, i) => PackageFixture.WriteFolder(workspace, "input/" + i, "car" + i)).ToArray();
        var window = new MainWindow(new VehicleConverter(temporaryParent: workspace.PathFor("temp")), new FakeDialogs());
        try
        {
            var model = (MainViewModel)window.DataContext;
            model.AddInputs(inputs); await model.AnalyzeCommand.ExecuteAsync(null);
            Assert.Equal(5, model.Packages.Count);
            model.DestinationDirectory = workspace.PathFor("output");
            for (var i = 0; i < names.Length; i++) model.Packages[i].ResourceName = names[i];
            model.SelectedPackage = model.Packages[1];
            await RenderPreviewAsync(window, "interface-nomes.png", 1120, 760);
            var table = (DataGrid)window.FindName("PackagesTable");
            Assert.All(table.Columns, c => Assert.True(c.ActualWidth > 20));
            Assert.InRange(table.Columns.Sum(c => c.ActualWidth), table.ActualWidth - 40, table.ActualWidth + 1);
            await RenderPreviewAsync(window, "interface-colecao.png", 1360, 880);
            await Dispatcher.Yield(DispatcherPriority.Background);
            var field = Descendants((FrameworkElement)window.Content).OfType<TextBox>().Single(box =>
                BindingOperations.GetBindingExpression(box, TextBox.TextProperty)?.ParentBinding.Path.Path == "SelectedPackage.ResourceName");
            field.Text = "Toyota-Chaser-JZX100";
            field.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            names[1] = field.Text;
            Assert.Equal("Pasta de saída: toyota-chaser-jzx100", model.SelectedPackage.ResourceNameFeedback);
            Assert.True(model.ExportCommand.CanExecute(null));
            await model.ExportCommand.ExecuteAsync(null);
            Assert.True(!model.Messages.Any(m => m.StartsWith("Erro")), string.Join(Environment.NewLine, model.Messages));
            for (var i = 0; i < names.Length; i++)
            {
                var normalized = ResourceNames.NormalizeInput(names[i]);
                Assert.Equal(normalized, model.Packages[i].ResourceName);
                Assert.Contains("ensure " + normalized, model.Messages);
                Assert.True(File.Exists(workspace.PathFor("output/" + normalized + "/stream/car" + i + ".yft")));
                Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(inputs[i], "data/vehicles.meta")),
                    await File.ReadAllBytesAsync(workspace.PathFor("output/" + normalized + "/data/vehicles.meta")));
                Assert.Equal("car" + i, Assert.Single(model.Packages[i].Vehicles).ModelName);
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task InvalidNamesDisableExportUntilCorrectedOrUnselectedAndPackNamesNormalize() => RunOnDispatcherAsync(async () =>
    {
        using var workspace = new TestWorkspace();
        var first = PackageFixture.WriteFolder(workspace, "first"); var second = PackageFixture.WriteFolder(workspace, "second", "other");
        var dialogs = new FakeDialogs();
        var window = new MainWindow(new VehicleConverter(temporaryParent: workspace.PathFor("temp")), dialogs);
        try
        {
            var model = (MainViewModel)window.DataContext; model.AddInputs([first, second]); await model.AnalyzeCommand.ExecuteAsync(null);
            var package = model.Packages[0]; model.SelectedPackage = package;
            foreach (var name in new[] { "", "CON", "../car", new string('a', 65) })
            {
                package.ResourceName = name;
                Assert.True(package.HasErrors); Assert.Equal("Nome inválido", package.State);
                Assert.False(model.CanExport); Assert.False(model.ExportCommand.CanExecute(null));
                await model.ExportCommand.ExecuteAsync(null);
                Assert.Equal(0, dialogs.FolderRequests);
            }
            package.ResourceName = "../car";
            await RenderPreviewAsync(window, "interface-nome-invalido.png", 1120, 800);
            package.IsSelected = false; Assert.True(model.ExportCommand.CanExecute(null));
            package.IsSelected = true; Assert.False(model.ExportCommand.CanExecute(null));
            package.ResourceName = "Car One"; Assert.True(model.ExportCommand.CanExecute(null));
            model.PackResourceName = "CON";
            Assert.True(model.ExportCommand.CanExecute(null)); // Pack name is irrelevant for separate exports.
            model.ExportAsPack = true; Assert.False(model.ExportCommand.CanExecute(null));
            Assert.Contains("reservado", model.ExportHint);
            model.PackResourceName = "My Garage"; await model.ValidateNowAsync(); Assert.True(model.ExportCommand.CanExecute(null));
            Assert.Contains("my_garage", model.ExportHint);
            model.DestinationDirectory = workspace.PathFor("packs");
            await model.ExportCommand.ExecuteAsync(null);
            Assert.Contains("ensure my_garage", model.Messages);
            Assert.True(File.Exists(workspace.PathFor("packs/my_garage/.fvpack.json")));
            model.ClearCommand.Execute(null); model.AddInputs([workspace.PathFor("packs/my_garage")]);
            await model.AnalyzeCommand.ExecuteAsync(null);
            Assert.Contains(model.Packages, p => p.ResourceName == "car_one");
            Assert.All(model.Packages, p => Assert.False(p.HasErrors));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task NormalizedNamesCannotOverwriteEachOtherInBatchExport() => RunOnDispatcherAsync(async () =>
    {
        using var workspace = new TestWorkspace();
        var first = PackageFixture.WriteFolder(workspace, "first"); var second = PackageFixture.WriteFolder(workspace, "second", "other");
        var window = new MainWindow(new VehicleConverter(temporaryParent: workspace.PathFor("temp")), new FakeDialogs());
        try
        {
            var model = (MainViewModel)window.DataContext; model.AddInputs([first, second]); await model.AnalyzeCommand.ExecuteAsync(null);
            model.Packages[0].ResourceName = "Car Name"; model.Packages[1].ResourceName = "car_name";
            model.DestinationDirectory = workspace.PathFor("output");
            await model.ExportCommand.ExecuteAsync(null);
            Assert.Contains(model.Messages, m => m.Contains("mesmo destino e nome"));
            Assert.False(Directory.Exists(workspace.PathFor("output")));
        }
        finally { window.Close(); }
    });
    [Fact]
    public Task TableSupportsSortingSelectionDetailsAndEmbeddedFont() => RunOnDispatcherAsync(async () =>
    {
        using var workspace = new TestWorkspace();
        var first = PackageFixture.WriteFolder(workspace,"first"); var second = PackageFixture.WriteFolder(workspace,"second","other");
        var window = new MainWindow(new VehicleConverter(temporaryParent:workspace.PathFor("temp")),new FakeDialogs());
        try
        {
            var model=(MainViewModel)window.DataContext;
            model.AddInputs([first,second]); await model.AnalyzeCommand.ExecuteAsync(null);
            model.Packages[0].ResourceName="Zulu"; model.Packages[1].ResourceName="Alpha";
            model.Packages[1].DestinationDirectory=workspace.PathFor("individual");
            await RenderPreviewAsync(window,"interface-tabela.png",1360,880);
            await Dispatcher.Yield(DispatcherPriority.Background);
            var table=Assert.IsType<DataGrid>(window.FindName("PackagesTable"));
            Assert.Equal(2,table.Items.Count); Assert.True(table.CanUserSortColumns);
            Assert.All(table.Columns,c=>Assert.True(c.ActualWidth > 20, $"Column {c.SortMemberPath}: {c.ActualWidth}"));
            Assert.InRange(table.Columns.Sum(c=>c.ActualWidth),table.ActualWidth-40,table.ActualWidth+1);
            Assert.Contains(table.Columns,c=>c.SortMemberPath=="TotalBytes");
            table.SelectedItem=model.Packages[1];
            Assert.Same(model.Packages[1],model.SelectedPackage);
            Assert.Equal(workspace.PathFor("individual"),model.SelectedPackage!.DestinationLabel);
            var header=Descendants(table).OfType<DataGridColumnHeader>().Single(h=>h.Column?.SortMemberPath=="ResourceName");
            var click=typeof(DataGridColumnHeader).GetMethod("OnClick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
            click.Invoke(header,null); await Dispatcher.Yield(DispatcherPriority.Background);
            Assert.Equal(ListSortDirection.Ascending,header.Column!.SortDirection);
            Assert.Equal("Alpha",Assert.IsType<PackageViewModel>(table.Items[0]).ResourceName);
            var all=Descendants(table).OfType<CheckBox>().Single(c=>
                BindingOperations.GetBindingExpression(c,ToggleButton.IsCheckedProperty)?.ParentBinding.Path.Path=="DataContext.TableSelection");
            all.IsChecked=false; all.GetBindingExpression(ToggleButton.IsCheckedProperty)!.UpdateSource();
            Assert.All(model.Packages,p=>Assert.False(p.IsSelected)); Assert.False(model.ExportCommand.CanExecute(null));
            all.IsChecked=true; all.GetBindingExpression(ToggleButton.IsCheckedProperty)!.UpdateSource();
            Assert.All(model.Packages,p=>Assert.True(p.IsSelected));
            all.IsChecked=null; all.GetBindingExpression(ToggleButton.IsCheckedProperty)!.UpdateSource();
            Assert.All(model.Packages,p=>Assert.False(p.IsSelected)); Assert.False(model.TableSelection);
            var rowToggle=Descendants(table).OfType<CheckBox>().First(c=>c.DataContext is PackageViewModel &&
                BindingOperations.GetBindingExpression(c,ToggleButton.IsCheckedProperty)?.ParentBinding.Path.Path=="IsSelected");
            rowToggle.IsChecked=true; rowToggle.GetBindingExpression(ToggleButton.IsCheckedProperty)!.UpdateSource();
            Assert.Null(model.TableSelection); Assert.True(model.ExportCommand.CanExecute(null));
            model.SearchText="other"; await Dispatcher.Yield(DispatcherPriority.Background);
            Assert.Single(table.Items.Cast<object>());
            model.SearchText=""; model.SelectAllCommand.Execute(null);
            foreach(var weight in new[]{FontWeights.Normal,FontWeights.Medium,FontWeights.SemiBold})
            {
                Assert.True(new Typeface(window.FontFamily,FontStyles.Normal,weight,FontStretches.Normal).TryGetGlyphTypeface(out var glyph));
                Assert.Contains("/assets/fonts/",glyph.FontUri.ToString(),StringComparison.OrdinalIgnoreCase);
                Assert.Contains("IBM Plex Sans",glyph.FamilyNames.Values);
            }
        }
        finally {window.Close();}
    });
    [Fact]
    public Task PrefersReadyVersionAndLeavesAddonAlternativeAvailableWithoutDoubleExport() => RunOnDispatcherAsync(async () =>
    {
        using var w=new TestWorkspace();
        var folder=PackageFixture.WriteFolder(w,"input");
        var converter=new VehicleConverter(temporaryParent:w.PathFor("temp"));
        using var source=await converter.AnalyzeAsync([folder]);
        var ready=await converter.ExportAsync(Assert.Single(source.Packages),new("ready",w.PathFor("ready_source"),new Dictionary<string,string>()));
        var window=new MainWindow(converter,new FakeDialogs());
        try
        {
            var model=(MainViewModel)window.DataContext;
            model.AddInputs([folder,ready.ResourceDirectory]); await model.AnalyzeCommand.ExecuteAsync(null);
            Assert.Equal(2,model.Packages.Count);
            Assert.False(model.Packages[0].Package.IsFromPack); Assert.True(model.Packages[1].Package.IsFromPack);
            Assert.True(model.Packages[0].IsSelected); Assert.False(model.Packages[1].IsSelected);
            Assert.Equal("DLC / RPF",model.Packages[0].SourceKind);
            Assert.Contains(model.Messages,m=>m.Contains("Alternativa desmarcada"));
            model.DestinationDirectory=w.PathFor("out"); await model.ExportCommand.ExecuteAsync(null);
            Assert.Single(Directory.GetDirectories(w.PathFor("out")));
            model.Packages[0].IsSelected=false; model.Packages[1].IsSelected=true;
            Assert.True(model.ExportCommand.CanExecute(null));
            await model.ExportCommand.ExecuteAsync(null);
            Assert.Equal(2,Directory.GetDirectories(w.PathFor("out")).Length);
        }
        finally {window.Close();}
    });
    [Fact]
    public Task ExplainsBlockedSelectionAndAppliesOnlyExplicitlyApprovedRepair() => RunOnDispatcherAsync(async () =>
    {
        using var w=new TestWorkspace();var folder=PackageFixture.WriteFolder(w,"input");
        w.WriteText("input/data/carcols.meta",MetadataRepairTests.Broken);
        var window=new MainWindow(new VehicleConverter(temporaryParent:w.PathFor("temp")),new FakeDialogs());
        try
        {
            var model=(MainViewModel)window.DataContext;model.AddInputs([folder]);await model.AnalyzeCommand.ExecuteAsync(null);
            var package=Assert.Single(model.Packages);
            Assert.False(package.IsSelected);Assert.True(package.HasImportErrors);Assert.True(package.CanRepair);
            Assert.False(model.ExportCommand.CanExecute(null));Assert.True(model.ShowActivity);
            Assert.Contains(model.Messages,m=>m.StartsWith("Bloqueado:")&&m.Contains("visibleMods"));
            Assert.Same(package,model.SelectedPackage);Assert.Contains("linkMods",package.SelectionHint);
            model.ShowActivity=false;await RenderPreviewAsync(window,"interface-correcao-xml.png",1120,760);
            var tabs=Descendants((FrameworkElement)window.Content).OfType<TabControl>().Single(); Assert.True(tabs.ActualHeight > 80);
            var checkbox=Descendants((FrameworkElement)window.Content).OfType<CheckBox>().Single(c=>c.DataContext==package);
            Assert.False(checkbox.IsEnabled);Assert.True(ToolTipService.GetShowOnDisabled(checkbox));
            Assert.Contains("linkMods",Assert.IsType<string>(checkbox.ToolTip));
            package.Vehicles[0].AudioNameHash="ADDER";
            model.ApproveRepairCommand.Execute(package);
            Assert.True(package.IsSelected);Assert.False(package.HasImportErrors);Assert.False(package.CanRepair);
            Assert.True(model.ExportCommand.CanExecute(null));
            model.DestinationDirectory=w.PathFor("out");await model.ExportCommand.ExecuteAsync(null);
            Assert.Equal(MetadataRepairTests.Broken,await File.ReadAllTextAsync(w.PathFor("input/data/carcols.meta")));
            Assert.Equal(MetadataRepairTests.Broken.Replace("</linkMods>","</visibleMods>"),await File.ReadAllTextAsync(w.PathFor("out/car/data/carcols.meta")));
            Assert.Contains("ADDER",await File.ReadAllTextAsync(w.PathFor("out/car/data/vehicles.meta")));
        }
        finally {window.Close();}
    });
    private static async Task RenderPreviewAsync(MainWindow window, string name, int width, int height)
    {
        var content = (FrameworkElement)window.Content;
        content.Margin = new Thickness(0);
        content.Width = width; content.Height = height;
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.Background);
        content.UpdateLayout();
        if (Environment.GetEnvironmentVariable("FIVEM_UI_PREVIEW_ROOT") is not { Length: > 0 } preview) return;
        Directory.CreateDirectory(preview);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
            drawing.DrawRectangle(new VisualBrush(content) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, width, height) }, null, new Rect(0, 0, width, height));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(preview, name)); encoder.Save(file);
    }

    private sealed class FakeDialogs : IFileDialogs
    {
        public Queue<string> Folders { get; } = new();
        public int FolderRequests { get; private set; }
        public string[] PickInputs() => [];
        public string? PickFolder(string title, FolderPurpose purpose=FolderPurpose.Export) { FolderRequests++; return Folders.TryDequeue(out var folder) ? folder : null; }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static Task RunOnDispatcherAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try { await action(); completion.TrySetResult(); }
                catch (Exception ex) { completion.TrySetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(60));
    }
}
