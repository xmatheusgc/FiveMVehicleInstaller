using System.Collections.ObjectModel;
using System.IO;
using System.Xml;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiveMVehicleInstaller.Core;

namespace FiveMVehicleInstaller.App.ViewModels;

public partial class MainViewModel
{
    private IWorkspacePreferences preferencesStore = new MemoryWorkspacePreferences();
    private WorkspacePreferences preferences = new();
    private readonly List<AnalysisSession> additionalSessions = [];
    private PackDocument? editingDocument;
    private readonly HashSet<string> analyzedEditInputs = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? validationCancellation;
    private int validationVersion;
    public bool PackValidationReady { get; private set; }
    public ObservableCollection<PackageViewModel> HighlightedPackages { get; } = [];
    public ObservableCollection<string> RecentExportDirectories { get; } = [];
    public ObservableCollection<string> PackValidationIssues { get; } = [];
    [ObservableProperty] private string bulkAudioName = "";
    [ObservableProperty] private string bulkFeedback = "";
    [ObservableProperty] private bool isCheckingPack;
    [ObservableProperty] private string packValidationSummary = "Marque os pacotes para verificar a união.";
    public bool IsEditingPack => editingDocument is not null;
    public bool CanChangePackTarget => !IsEditingPack;
    public string EditingPackLabel => editingDocument?.Directory ?? "";
    public bool HasPackConflicts => PackValidationIssues.Count > 0;
    public bool HasHighlightedPackages => HighlightedPackages.Count > 0;
    public bool HasMultipleHighlighted => HighlightedPackages.Count > 1;
    public string HighlightedSummary => $"{HighlightedPackages.Count} pacote(s) destacado(s) · Ctrl/Shift para selecionar";
    private void InitializePreferences(IWorkspacePreferences? store)
    {
        preferencesStore=store??new MemoryWorkspacePreferences();preferences=preferencesStore.Load();

        foreach(var path in (preferences.RecentExportDirectories??[]).Where(p=>!string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).Take(8))RecentExportDirectories.Add(path);
        DestinationDirectory=preferences.ExportDirectory??"";
    }
    private void SavePreferences()
    {
        try{preferencesStore.Save(preferences);}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException)
        {var message="Não foi possível guardar as pastas: "+e.Message;if(!Messages.Contains(message))Messages.Add(message);}
    }
    private void RememberImport(IEnumerable<string> paths)
    {
        var last=paths.LastOrDefault();if(string.IsNullOrWhiteSpace(last))return;
        var path=Directory.Exists(last)?Path.GetFullPath(last):Path.GetDirectoryName(Path.GetFullPath(last));
        if(path is not null){preferences=preferences with{ImportDirectory=path};SavePreferences();}
    }
    private void RememberExport(string directory)
    {
        if(string.IsNullOrWhiteSpace(directory))return;
        var recent=RecentExportDirectories.Where(p=>!p.Equals(directory,StringComparison.OrdinalIgnoreCase)).Prepend(directory).Take(8).ToArray();
        RecentExportDirectories.Clear();foreach(var path in recent)RecentExportDirectories.Add(path);
        preferences=preferences with{ExportDirectory=directory,RecentExportDirectories=recent};SavePreferences();
    }
    public void SetHighlightedPackages(IEnumerable<PackageViewModel> packages)
    {
        HighlightedPackages.Clear();foreach(var package in packages.Where(Packages.Contains))HighlightedPackages.Add(package);
        OnPropertyChanged(nameof(HasHighlightedPackages));OnPropertyChanged(nameof(HasMultipleHighlighted));OnPropertyChanged(nameof(HighlightedSummary));
        ApplyBulkSoundCommand.NotifyCanExecuteChanged();RestoreBulkSoundCommand.NotifyCanExecuteChanged();
        PickBulkDestinationCommand.NotifyCanExecuteChanged();IncludeHighlightedCommand.NotifyCanExecuteChanged();ExcludeHighlightedCommand.NotifyCanExecuteChanged();
    }
    private bool CanApplyBulk => CanRun&&HasHighlightedPackages;
    [RelayCommand(CanExecute=nameof(CanApplyBulk))] private void ApplyBulkSound()
    {
        var value=BulkAudioName.Trim();
        try{if(value.Length>256||value.Any(char.IsControl))throw new ArgumentException("Som inválido: use até 256 caracteres sem controles.");XmlConvert.VerifyXmlChars(value);}
        catch(Exception e) when(e is ArgumentException or XmlException){BulkFeedback=e.Message;return;}
        var count=0;
        foreach(var package in HighlightedPackages)foreach(var vehicle in package.Vehicles){vehicle.AudioNameHash=value;count++;}
        BulkFeedback=$"Som aplicado a {count} veículo(s). Será gravado somente ao exportar ou salvar.";
    }
    [RelayCommand(CanExecute=nameof(CanApplyBulk))] private void RestoreBulkSound()
    {
        foreach(var package in HighlightedPackages)foreach(var vehicle in package.Vehicles)vehicle.AudioNameHash=vehicle.OriginalAudio;
        BulkFeedback="Sons restaurados para os valores importados.";
    }
    [RelayCommand(CanExecute=nameof(CanApplyBulk))] private void PickBulkDestination()
    {
        if(dialogs.PickFolder("Destino dos pacotes destacados") is not { } directory)return;
        foreach(var package in HighlightedPackages)package.DestinationDirectory=directory;
        RememberExport(directory);BulkFeedback="Destino aplicado aos pacotes destacados no modo separado.";
    }
    [RelayCommand(CanExecute=nameof(CanApplyBulk))] private void IncludeHighlighted()
    {foreach(var package in HighlightedPackages.Where(p=>p.Package.CanExport))package.IsSelected=true;}
    [RelayCommand(CanExecute=nameof(CanApplyBulk))] private void ExcludeHighlighted()
    {foreach(var package in HighlightedPackages)package.IsSelected=false;}
    private PackageExportRequest[] SelectedRequests() => Packages.Where(p=>p.IsSelected&&p.Package.CanExport)
        .Select(p=>new PackageExportRequest(p.Package,p.OutputResourceName,p.Vehicles.ToDictionary(v=>v.Definition.Key,v=>v.AudioNameHash))).ToArray();
    private void QueueValidation()
    {
        if(IsBusy)return;
        _=ValidateSelectionAsync(debounce:true);
    }
    public Task ValidateNowAsync() => ValidateSelectionAsync(debounce:false);
    [RelayCommand(CanExecute=nameof(CanRun))] private Task RecheckPackAsync() => ValidateNowAsync();
    private async Task ValidateSelectionAsync(bool debounce)
    {
        validationCancellation?.Cancel();validationCancellation?.Dispose();
        var source=validationCancellation=new CancellationTokenSource();var version=++validationVersion;
        PackValidationReady=false;IsCheckingPack=true;PackValidationSummary="Verificando modelos, tuning, layouts e assets…";RefreshSummary();
        try
        {
            if(debounce)await Task.Delay(250,source.Token);
            var requests=SelectedRequests();
            var result=await converter.ValidatePackAsync(requests,source.Token);
            if(version!=validationVersion)return;
            PackValidationIssues.Clear();
            foreach(var issue in result.Issues)PackValidationIssues.Add(issue.Message+(issue.Packages.Count>0?" — "+string.Join(", ",issue.Packages):""));
            PackValidationReady=true;
            PackValidationSummary=requests.Length==0?"Marque pelo menos um pacote.":result.CanExport
                ?$"União verificada: {requests.Length} pacote(s), sem conflitos detectados."
                :$"União bloqueada: {result.Issues.Count} conflito(s). Revise os pacotes antes de configurar o pack.";
        }
        catch(OperationCanceledException){ if(version==validationVersion)PackValidationSummary="Verificação cancelada. Altere a seleção para verificar novamente."; }
        catch(Exception e)
        {
            if(version!=validationVersion)return;
            PackValidationIssues.Clear();PackValidationIssues.Add(e.Message);PackValidationSummary="Não foi possível verificar a união. Analise novamente.";
        }
        finally
        {
            if(version==validationVersion){IsCheckingPack=false;OnPropertyChanged(nameof(HasPackConflicts));RefreshSummary();}
        }
    }
    private void RefreshPackMode()
    {
        foreach(var property in new[]{nameof(IsEditingPack),nameof(CanChangePackTarget),nameof(EditingPackLabel),nameof(ExportLabel)})OnPropertyChanged(property);
        SaveAsNewPackCommand.NotifyCanExecuteChanged();
    }
    [RelayCommand(CanExecute=nameof(CanRun))] private async Task OpenPackAsync()
    {
        if(dialogs.PickFolder("Abra um pack criado pelo aplicativo",FolderPurpose.Pack) is { } path)await LoadPackAsync(path);
    }
    public async Task LoadPackAsync(string path)
    {
        if(IsBusy)return;
        var progress=BeginOperation("Abrindo o índice do pack…");
        try
        {
            var opened=await converter.OpenPackAsync(path,progress,cancellation!.Token);
            ReleasePackages();Inputs.Clear();Messages.Clear();analyzedEditInputs.Clear();session=opened.Session;editingDocument=opened.Document;
            foreach(var package in opened.Session.Packages){var item=new PackageViewModel(package);item.PropertyChanged+=PackageChanged;Packages.Add(item);}
            foreach(var diagnostic in opened.Session.Diagnostics)Messages.Add(FormatDiagnostic(diagnostic));
            DestinationDirectory=Path.GetDirectoryName(opened.Document.Directory)!;PackResourceName=opened.Document.ResourceName;ExportAsPack=true;
            preferences=preferences with{PackDirectory=opened.Document.Directory};SavePreferences();
            SelectedPackage=Packages.FirstOrDefault();HasPendingInputs=false;RefreshPackMode();RefreshSummary();
            await ValidateNowAsync();Status=$"Pack aberto: {Packages.Count} pacote(s). Desmarque para remover ou adicione novos RPFs.";
        }
        catch(OperationCanceledException){Status="Abertura cancelada.";}
        catch(Exception e){Messages.Add("Erro ao abrir pack: "+e.Message);ShowActivity=true;Status="Não foi possível abrir o pack.";}
        finally{EndOperation();}
    }
    private bool CanSaveAsNewPack => CanRun&&IsEditingPack;
    [RelayCommand(CanExecute=nameof(CanSaveAsNewPack))] private void SaveAsNewPack()
    {
        editingDocument=null;PackResourceName=OutputPackName[..Math.Min(58,OutputPackName.Length)]+"_novo";
        RefreshPackMode();Status="Escolha o destino e exporte como um novo pack.";
    }
    private void ReleaseFeatureState()
    {
        validationCancellation?.Cancel();validationCancellation?.Dispose();validationCancellation=null;validationVersion++;
        foreach(var item in additionalSessions)item.Dispose();additionalSessions.Clear();SetHighlightedPackages([]);
        PackValidationIssues.Clear();IsCheckingPack=false;PackValidationReady=false;editingDocument=null;analyzedEditInputs.Clear();RefreshPackMode();
    }
}