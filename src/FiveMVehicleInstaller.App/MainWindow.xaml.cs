using FiveMVehicleInstaller.Core;
using System.ComponentModel;
using System.Windows;
using FiveMVehicleInstaller.App.ViewModels;
using FiveMVehicleInstaller.Infrastructure;
using Microsoft.Win32;

namespace FiveMVehicleInstaller.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    public MainWindow() : this(new VehicleConverter(), null, new JsonWorkspacePreferences()) { }
    public MainWindow(IVehicleConverter converter, IFileDialogs? dialogs, IWorkspacePreferences? preferences=null)
    {
        InitializeComponent(); DataContext = viewModel = new MainViewModel(converter, dialogs ?? new FileDialogs(this, preferences ?? new MemoryWorkspacePreferences()), preferences);
    }
    private void OnPackageSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if(DataContext is MainViewModel model)model.SetHighlightedPackages(PackagesTable.SelectedItems.Cast<PackageViewModel>().ToArray());
    }
    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) viewModel.AddInputs(paths);
        e.Handled = true;
    }
    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = !viewModel.IsBusy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (viewModel.IsBusy) { viewModel.CancelCommand.Execute(null); e.Cancel = true; return; }
        viewModel.Dispose();
    }
}

internal sealed class FileDialogs(Window owner, IWorkspacePreferences preferences) : IFileDialogs
{
    private static string Existing(string? path)=>!string.IsNullOrWhiteSpace(path)&&System.IO.Directory.Exists(path)?path:"";
    public string[] PickInputs()
    {
        var dialog = new OpenFileDialog { Title = "Selecione os mods de veículos", Multiselect = true,
            InitialDirectory = Existing(preferences.Load().ImportDirectory), Filter = "Mods de veículos (RPF/Add-On)|*.rpf;*.zip;*.rar;*.7z" };
        return dialog.ShowDialog(owner) == true ? dialog.FileNames : [];
    }
    public string? PickFolder(string title, FolderPurpose purpose=FolderPurpose.Export)
    {
        var state=preferences.Load();
        var path=purpose switch {FolderPurpose.Import=>state.ImportDirectory,FolderPurpose.Pack=>state.PackDirectory,_=>state.ExportDirectory};
        var dialog = new OpenFolderDialog { Title = title, InitialDirectory=Existing(path) };
        return dialog.ShowDialog(owner) == true ? dialog.FolderName : null;
    }
}
