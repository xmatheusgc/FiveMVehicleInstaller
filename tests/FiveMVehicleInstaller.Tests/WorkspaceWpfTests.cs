using System.IO;
using System.Windows;
using System.Windows.Controls;
using FiveMVehicleInstaller.App;
using FiveMVehicleInstaller.App.ViewModels;
using FiveMVehicleInstaller.Core;
using FiveMVehicleInstaller.Infrastructure;

namespace FiveMVehicleInstaller.Tests;

public sealed partial class WpfTests
{
    [Fact]
    public Task HighlightsRowsForBulkEditsWithoutChangingExportCheckmarksAndRemembersFolders() => RunOnDispatcherAsync(async()=>
    {
        using var w=new TestWorkspace();var first=PackageFixture.WriteFolder(w,"first");var second=PackageFixture.WriteFolder(w,"second","other");
        var store=new JsonWorkspacePreferences(w.PathFor("settings/workspace.json"));var dialogs=new FakeDialogs();dialogs.Folders.Enqueue(w.PathFor("chosen"));
        var converter=new VehicleConverter(temporaryParent:w.PathFor("temp"));var window=new MainWindow(converter,dialogs,store);
        try
        {
            var model=(MainViewModel)window.DataContext;model.AddInputs([first,second]);await model.AnalyzeCommand.ExecuteAsync(null);
            model.Packages[1].IsSelected=false;await RenderPreviewAsync(window,"interface-edicao-lote.png",1360,880);
            var table=(DataGrid)window.FindName("PackagesTable");Assert.Equal(DataGridSelectionMode.Extended,table.SelectionMode);
            table.SelectedItems.Clear();table.SelectedItems.Add(model.Packages[0]);table.SelectedItems.Add(model.Packages[1]);
            Assert.Equal(2,model.HighlightedPackages.Count);Assert.False(model.Packages[1].IsSelected);
            model.BulkAudioName="ADDER";model.ApplyBulkSoundCommand.Execute(null);
            Assert.All(model.Packages,p=>Assert.All(p.Vehicles,v=>Assert.Equal("ADDER",v.AudioNameHash)));
            Assert.All(new[]{first,second},p=>Assert.Contains("SULTAN",File.ReadAllText(Path.Combine(p,"data/vehicles.meta"))));
            model.PickBulkDestinationCommand.Execute(null);Assert.All(model.Packages,p=>Assert.Equal(w.PathFor("chosen"),p.DestinationDirectory));
            model.IncludeHighlightedCommand.Execute(null);model.DestinationDirectory=w.PathFor("default");await model.ExportCommand.ExecuteAsync(null);
            Assert.True(File.Exists(w.PathFor("chosen/car/fxmanifest.lua")));Assert.True(File.Exists(w.PathFor("chosen/other/fxmanifest.lua")));
            await model.ValidateNowAsync(); await RenderPreviewAsync(window,"interface-edicao-lote.png",1120,760);
            var remembered=new JsonWorkspacePreferences(w.PathFor("settings/workspace.json")).Load();Assert.Equal(second,remembered.ImportDirectory);Assert.Equal(w.PathFor("default"),remembered.ExportDirectory);
            var next=new MainViewModel(converter,new FakeDialogs(),store);try{Assert.Equal(w.PathFor("default"),next.DestinationDirectory);}finally{next.Dispose();}
        }
        finally{window.Close();}
    });
    [Fact]
    public Task ShowsConflictBeforePackConfigurationAndClearsItWhenSelectionChanges() => RunOnDispatcherAsync(async()=>
    {
        using var w=new TestWorkspace();var first=PackageFixture.WriteFolder(w,"first");var second=PackageFixture.WriteFolder(w,"second","other");
        w.Write("first/shared.yft",RpfFixture.Resource([1]));w.Write("second/shared.yft",RpfFixture.Resource([2]));
        var window=new MainWindow(new VehicleConverter(temporaryParent:w.PathFor("temp")),new FakeDialogs());
        try
        {
            var model=(MainViewModel)window.DataContext;model.AddInputs([first,second]);await model.AnalyzeCommand.ExecuteAsync(null);
            Assert.True(model.HasPackConflicts);Assert.Contains(model.PackValidationIssues,i=>i.Contains("shared.yft"));
            Assert.Equal("",model.DestinationDirectory);model.ExportAsPack=true;Assert.False(model.ExportCommand.CanExecute(null));
            await RenderPreviewAsync(window,"interface-conflitos-antecipados.png",1120,760);
            model.Packages[1].IsSelected=false;await model.ValidateNowAsync();Assert.False(model.HasPackConflicts);Assert.True(model.ExportCommand.CanExecute(null));
            model.DestinationDirectory=w.PathFor("out");await model.ExportCommand.ExecuteAsync(null);Assert.True(File.Exists(w.PathFor("out/pack_veiculos/fxmanifest.lua")));
        }
        finally{window.Close();}
    });
    [Fact]
    public Task OpensPackAddsRpfRemovesVehicleAndSavesAgainThroughCommands() => RunOnDispatcherAsync(async()=>
    {
        using var w=new TestWorkspace();var converter=new VehicleConverter(temporaryParent:w.PathFor("temp"));
        using(var inputs=await converter.AnalyzeAsync([PackageFixture.WriteFolder(w,"a"),PackageFixture.WriteFolder(w,"b","other")]))
            await converter.ExportPackAsync(inputs.Packages.Select(p=>new PackageExportRequest(p,p.SuggestedResourceName,new Dictionary<string,string>())).ToArray(),new("garage",w.PathFor("packs")));
        var window=new MainWindow(converter,new FakeDialogs());
        try
        {
            var model=(MainViewModel)window.DataContext;await model.LoadPackAsync(w.PathFor("packs/garage"));Assert.True(model.IsEditingPack);Assert.Equal("Salvar alterações",model.ExportLabel);
            Assert.All(model.Packages,p=>Assert.Equal("Do pack",p.SourceKind));
            var retained=model.Packages.Single(p=>p.Vehicles[0].ModelName=="other");retained.Vehicles[0].AudioNameHash="ADDER";
            var added=w.Write("newcar/dlc.rpf",RpfFixture.Create(PackageFixture.Files("third")));model.AddInputs([added]);await model.AnalyzeCommand.ExecuteAsync(null);
            Assert.Equal(3,model.Packages.Count);Assert.Same(retained,model.Packages.Single(p=>p.Vehicles[0].ModelName=="other"));Assert.Equal("ADDER",retained.Vehicles[0].AudioNameHash);
            model.Packages.Single(p=>p.Vehicles[0].ModelName=="car").IsSelected=false;await model.ValidateNowAsync();
            await RenderPreviewAsync(window,"interface-editar-pack.png",1360,880);await model.ExportCommand.ExecuteAsync(null);
            Assert.True(model.IsEditingPack);Assert.Equal(2,model.Packages.Count);Assert.DoesNotContain(model.Packages,p=>p.Vehicles[0].ModelName=="car");
            Assert.Equal("ADDER",model.Packages.Single(p=>p.Vehicles[0].ModelName=="other").Vehicles[0].AudioNameHash);
            await model.ExportCommand.ExecuteAsync(null);Assert.Contains("atualizado",model.Status);
            model.SaveAsNewPackCommand.Execute(null);Assert.False(model.IsEditingPack);Assert.Equal("garage_novo",model.OutputPackName);
            await model.ValidateNowAsync();await model.ExportCommand.ExecuteAsync(null);Assert.True(File.Exists(w.PathFor("packs/garage_novo/.fvpack.json")));
        }
        finally{window.Close();}
    });
    [Fact]
    public void CorruptPreferencesFallBackAndFolderHistoryIsBounded()
    {
        using var w=new TestWorkspace();var path=w.WriteText("settings/workspace.json","{broken");var store=new JsonWorkspacePreferences(path);
        Assert.Equal("",store.Load().ExportDirectory);
        var model=new MainViewModel(new VehicleConverter(),new FakeDialogs(),store);
        try{for(var i=0;i<12;i++)model.DestinationDirectory=w.PathFor("out/"+i);Assert.Equal(8,store.Load().RecentExportDirectories.Length);}
        finally{model.Dispose();}
    }
    [Fact]
    public Task AddingInputPreservesNamesSoundsDestinationsAndSelectionAlreadyConfigured() => RunOnDispatcherAsync(async()=>
    {
        using var w=new TestWorkspace();var first=PackageFixture.WriteFolder(w,"first");
        var window=new MainWindow(new VehicleConverter(temporaryParent:w.PathFor("temp")),new FakeDialogs());
        try
        {
            var model=(MainViewModel)window.DataContext;model.AddInputs([first]);await model.AnalyzeCommand.ExecuteAsync(null);
            var previous=model.Packages[0];previous.ResourceName="meu_carro";previous.DestinationDirectory=w.PathFor("individual");previous.IsSelected=false;previous.Vehicles[0].AudioNameHash="ADDER";
            model.AddInputs([PackageFixture.WriteFolder(w,"second","other")]);await model.AnalyzeCommand.ExecuteAsync(null);
            var preserved=model.Packages.Single(p=>p.Vehicles[0].ModelName=="car");
            Assert.Equal("meu_carro",preserved.ResourceName);Assert.Equal(w.PathFor("individual"),preserved.DestinationDirectory);Assert.False(preserved.IsSelected);Assert.Equal("ADDER",preserved.Vehicles[0].AudioNameHash);
        }
        finally{window.Close();}
    });
    [Fact]
    public Task CancellingPreflightCannotContinueIntoExportAndCanBeRetried() => RunOnDispatcherAsync(async()=>
    {
        using var w=new TestWorkspace();var converter=new DelayedValidationConverter(new VehicleConverter(temporaryParent:w.PathFor("temp")));
        var window=new MainWindow(converter,new FakeDialogs());
        try
        {
            var model=(MainViewModel)window.DataContext;model.AddInputs([PackageFixture.WriteFolder(w,"input")]);await model.AnalyzeCommand.ExecuteAsync(null);
            model.ExportAsPack=true;model.DestinationDirectory=w.PathFor("out");converter.Delay=true;
            var export=model.ExportCommand.ExecuteAsync(null);Assert.True(model.IsBusy);Assert.False(model.CanRun);
            model.CancelCommand.Execute(null);await export;Assert.Contains("cancelada",model.Status);Assert.False(Directory.Exists(w.PathFor("out")));
            Assert.False(model.PackValidationReady);converter.Delay=false;await model.RecheckPackCommand.ExecuteAsync(null);
            Assert.True(model.PackValidationReady);Assert.True(model.ExportCommand.CanExecute(null));
            await model.ExportCommand.ExecuteAsync(null);Assert.True(File.Exists(w.PathFor("out/pack_veiculos/fxmanifest.lua")));
        }
        finally{window.Close();}
    });
    private sealed class DelayedValidationConverter(IVehicleConverter inner):IVehicleConverter
    {
        public bool Delay {get;set;}
        public async Task<PackValidationResult> ValidatePackAsync(IReadOnlyList<PackageExportRequest> packages,CancellationToken cancellationToken=default)
        {if(Delay)await Task.Delay(Timeout.Infinite,cancellationToken);return await inner.ValidatePackAsync(packages,cancellationToken);}
        public Task<AnalysisSession> AnalyzeAsync(IEnumerable<string> inputs,IProgress<ConversionProgress>? progress=null,CancellationToken cancellationToken=default)=>inner.AnalyzeAsync(inputs,progress,cancellationToken);
        public Task<ExportResult> ExportAsync(VehiclePackage package,ExportOptions options,IProgress<ConversionProgress>? progress=null,CancellationToken cancellationToken=default)=>inner.ExportAsync(package,options,progress,cancellationToken);
        public Task<ExportResult> ExportPackAsync(IReadOnlyList<PackageExportRequest> packages,PackExportOptions options,IProgress<ConversionProgress>? progress=null,CancellationToken cancellationToken=default)=>inner.ExportPackAsync(packages,options,progress,cancellationToken);
        public Task<OpenPackResult> OpenPackAsync(string directory,IProgress<ConversionProgress>? progress=null,CancellationToken cancellationToken=default)=>inner.OpenPackAsync(directory,progress,cancellationToken);
        public Task<PackUpdateResult> UpdatePackAsync(PackDocument document,IReadOnlyList<PackageExportRequest> packages,IProgress<ConversionProgress>? progress=null,CancellationToken cancellationToken=default)=>inner.UpdatePackAsync(document,packages,progress,cancellationToken);
    }}