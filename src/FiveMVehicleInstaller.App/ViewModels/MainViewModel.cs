using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiveMVehicleInstaller.Core;

namespace FiveMVehicleInstaller.App.ViewModels;

public interface IFileDialogs
{
    string[] PickInputs();
    string? PickFolder(string title, FolderPurpose purpose = FolderPurpose.Export);
}

public partial class VehicleViewModel(VehicleDefinition definition) : ObservableObject
{
    public VehicleDefinition Definition { get; } = definition;
    public string ModelName => Definition.ModelName;
    public string OriginalAudio => Definition.AudioNameHash;
    [ObservableProperty] private string audioNameHash = definition.AudioNameHash;
}

public partial class PackageViewModel : ObservableObject
{
    public VehiclePackage Package { get; private set; }
    public ObservableCollection<VehicleViewModel> Vehicles { get; }
    public string SourceKind => Package.IsFromPack ? "Do pack" : "DLC / RPF";
    public string CreatorSummary => Package.Credits.Count == 0 ? "Não identificada" :
        string.Join(", ", Package.Credits.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase));
    public string CreditDetails => Package.Credits.Count == 0 ? "Nenhuma atribuição explícita encontrada nos arquivos analisados." :
        string.Join("\n\n", Package.Credits.Select(c => $"{c.Name} · {c.Role}\nFonte: {c.SourcePath}\nTrecho: {c.Evidence}"));
    public int VehicleCount => Vehicles.Count;
    public int FileCount => Package.Files.Count;
    public long TotalBytes => Package.Files.Sum(f => f.Content.Length);
    public string SizeLabel => MainViewModel.FormatBytes(TotalBytes);
    public string ModelNames => string.Join(", ", Vehicles.Select(v => v.ModelName));
    public string Summary => $"{Vehicles.Count} veículo(s) · {Package.Files.Count} arquivos · {MainViewModel.FormatBytes(Package.Files.Sum(f => f.Content.Length))}";
    public string State => HasNameError ? "Nome inválido" : !Package.CanExport ? "Requer atenção" : LastExportPath.Length > 0 ? "Exportado" : "Pronto";
    public bool HasErrors => !Package.CanExport || HasNameError;
    public string[] FilePaths => Package.Files.Select(f => f.DestinationPath).ToArray();
    public string[] Diagnostics => Package.Diagnostics.OrderByDescending(d => d.Severity).Select(MainViewModel.FormatDiagnostic).ToArray();
    public bool HasImportErrors => !Package.CanExport;
    public string BlockReason => string.Join("\n", Package.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message));
    public string SelectionHint => Package.CanExport ? "Incluir este pacote na exportação" : BlockReason;
    public bool CanRepair => Package.SuggestedRepair is not null;
    public string RepairDescription => Package.SuggestedRepair?.Description ?? "";
    public void ApplyApprovedRepair()
    {
        if (Package.SuggestedRepair is not { } proposal) return;
        var repaired = proposal.RepairedPackage;
        Package = new VehiclePackage
        {
            SourceName = repaired.SourceName, IsFromPack = repaired.IsFromPack, Credits = repaired.Credits,
            SuggestedResourceName = repaired.SuggestedResourceName, Vehicles = repaired.Vehicles, Files = repaired.Files,
            Diagnostics = repaired.Diagnostics.Concat(Package.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning))
                .Append(new(DiagnosticSeverity.Warning, "Correção aprovada para a cópia exportada: " + proposal.Description)).Distinct().ToArray()
        };
        IsSelected = Package.CanExport;
        foreach (var property in new[] { nameof(Package), nameof(FileCount), nameof(TotalBytes), nameof(SizeLabel), nameof(Summary),
            nameof(State), nameof(HasErrors), nameof(FilePaths), nameof(Diagnostics), nameof(HasImportErrors), nameof(BlockReason),
            nameof(SelectionHint), nameof(CanRepair), nameof(RepairDescription), nameof(CreatorSummary), nameof(CreditDetails) }) OnPropertyChanged(property);
    }
    public string DestinationLabel => string.IsNullOrWhiteSpace(DestinationDirectory) ? "Pasta padrão" : DestinationDirectory;
    [ObservableProperty] private string resourceName;
    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private string destinationDirectory = "";
    [ObservableProperty] private string lastExportPath = "";
    public string OutputResourceName => ResourceNames.NormalizeInput(ResourceName);
    public string ResourceNameError => ResourceNames.ValidationError(OutputResourceName);
    public bool HasNameError => ResourceNameError.Length > 0;
    public string ResourceNameFeedback => HasNameError ? ResourceNameError : "Pasta de saída: " + OutputResourceName;
    partial void OnResourceNameChanged(string value)
    {
        foreach (var property in new[] { nameof(OutputResourceName), nameof(ResourceNameError), nameof(HasNameError),
            nameof(ResourceNameFeedback), nameof(State), nameof(HasErrors) }) OnPropertyChanged(property);
    }
    partial void OnDestinationDirectoryChanged(string value) => OnPropertyChanged(nameof(DestinationLabel));
    partial void OnLastExportPathChanged(string value) => OnPropertyChanged(nameof(State));
    public PackageViewModel(VehiclePackage package)
    {
        Package = package; resourceName = ResourceNames.NormalizeInput(package.SuggestedResourceName); isSelected = package.CanExport;
        Vehicles = new(package.Vehicles.Select(v => new VehicleViewModel(v)));
    }
}

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IVehicleConverter converter;
    private readonly IFileDialogs dialogs;
    private AnalysisSession? session;
    private CancellationTokenSource? cancellation;
    public ObservableCollection<string> Inputs { get; } = [];
    public ObservableCollection<PackageViewModel> Packages { get; } = [];
    public ObservableCollection<string> Messages { get; } = [];
    public ICollectionView PackagesView { get; }
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool hasPendingInputs;
    [ObservableProperty] private bool showActivity;
    [ObservableProperty] private string status = "Adicione mods ou uma pasta de resources para começar.";
    [ObservableProperty] private double progressValue;
    [ObservableProperty] private bool isIndeterminate;
    [ObservableProperty] private string destinationDirectory = "";
    [ObservableProperty] private PackageViewModel? selectedPackage;
    [ObservableProperty] private bool exportAsPack;
    [ObservableProperty] private string packResourceName = "pack_veiculos";
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private string lastEnsureCommand = "";
    public bool CanRun => !IsBusy;
    public bool CanAnalyze => !IsBusy && Inputs.Count > 0;
    public bool CanExport => !IsBusy && !HasPendingInputs && Packages.Any(p => p.IsSelected && p.Package.CanExport) && ExportNameError.Length == 0 && (!ExportAsPack || (PackValidationReady && !IsCheckingPack && !HasPackConflicts));
    public string OutputPackName => ResourceNames.NormalizeInput(PackResourceName);
    public string PackNameError => ResourceNames.ValidationError(OutputPackName);
    public bool HasPackNameError => PackNameError.Length > 0;
    public string ExportNameError => Packages.FirstOrDefault(p => p.IsSelected && p.Package.CanExport && p.HasNameError) is { } invalid
        ? "Corrija o nome do pacote selecionado: " + invalid.ResourceName + ". " + invalid.ResourceNameError
        : ExportAsPack && HasPackNameError ? "Nome do pack: " + PackNameError : "";
    public bool HasExportNameError => ExportNameError.Length > 0;
    public string ExportHint => ExportNameError.Length > 0 ? ExportNameError : ExportAsPack
        ? OutputDescription + " Pasta: " + OutputPackName : OutputDescription;
    partial void OnPackResourceNameChanged(string value)
    {
        foreach (var property in new[] { nameof(OutputPackName), nameof(PackNameError), nameof(HasPackNameError) }) OnPropertyChanged(property);
        RefreshSummary();
    }
    public bool CanCopyEnsure => LastEnsureCommand.Length > 0;
    public bool HasPackages => Packages.Count > 0;
    public bool NoPackages => !HasPackages;
    public bool HasSelectedPackage => SelectedPackage is not null;
    public bool ExportSeparately { get => !ExportAsPack; set { if (value) ExportAsPack = false; } }
    public int PackageCount => Packages.Count;
    public int VehicleCount => Packages.Sum(p => p.Vehicles.Count);
    public string CollectionSize => FormatBytes(Packages.Sum(p => p.Package.Files.Sum(f => f.Content.Length)));
    public string CollectionSummary => $"{PackageCount} pacotes · {VehicleCount} veículos · {CollectionSize}";
    public bool NoSelectedPackage => !HasSelectedPackage;
    public bool? TableSelection
    {
        get
        {
            var ready = Packages.Where(p => p.Package.CanExport).ToArray();
            var selected = ready.Count(p => p.IsSelected);
            return selected == 0 ? false : selected == ready.Length ? true : null;
        }
        set
        {
            if (IsBusy) return;
            foreach (var package in Packages.Where(p => p.Package.CanExport)) package.IsSelected = value ?? false;
            OnPropertyChanged(nameof(TableSelection));
        }
    }
    public string SelectionSummary => $"{Packages.Count(p => p.IsSelected && p.Package.CanExport)} de {Packages.Count} pacotes selecionados";
    public string InputSummary => Inputs.Count == 0 ? "RPF, ZIP, RAR, 7z ou pasta de DLCs" : $"{Inputs.Count} entrada(s) adicionada(s)";
    public string ExportLabel => ExportAsPack ? (IsEditingPack ? "Salvar alterações" : "Montar pack") : "Exportar selecionados";
    public string OutputDescription => ExportAsPack ? "Um resource, organizado por pacote de origem. Reimporte o pack para separá-lo novamente." :
        "Um resource por pacote. Cada um pode usar a pasta padrão ou um destino individual.";
    public string SelectedTarget => SelectedPackage is null ? "" :
        (string.IsNullOrWhiteSpace(SelectedPackage.DestinationDirectory) ? DestinationDirectory : SelectedPackage.DestinationDirectory).TrimEnd('\\', '/') + "\\" + SelectedPackage.OutputResourceName;

    public MainViewModel(IVehicleConverter converter, IFileDialogs dialogs, IWorkspacePreferences? preferences = null)
    {
        this.converter = converter; this.dialogs = dialogs; InitializePreferences(preferences);
        PackagesView = CollectionViewSource.GetDefaultView(Packages);
        PackagesView.Filter = item => item is PackageViewModel package &&
            (string.IsNullOrWhiteSpace(SearchText) || package.ResourceName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
             package.Vehicles.Any(v => v.ModelName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)));
    }
    public static string FormatBytes(long bytes) => bytes >= 1024L * 1024 * 1024 ? $"{bytes / (1024d * 1024 * 1024):0.0} GB" :
        bytes >= 1024 * 1024 ? $"{bytes / (1024d * 1024):0.0} MB" : $"{bytes / 1024d:0.#} KB";
    partial void OnSearchTextChanged(string value) => PackagesView.Refresh();
    partial void OnLastEnsureCommandChanged(string value) => CopyEnsureCommand.NotifyCanExecuteChanged();
    partial void OnHasPendingInputsChanged(bool value) => ExportCommand.NotifyCanExecuteChanged();
    partial void OnDestinationDirectoryChanged(string value) { OnPropertyChanged(nameof(SelectedTarget)); RememberExport(value); }
    partial void OnSelectedPackageChanged(PackageViewModel? value)
    { OnPropertyChanged(nameof(HasSelectedPackage)); OnPropertyChanged(nameof(NoSelectedPackage)); OnPropertyChanged(nameof(SelectedTarget)); }
    partial void OnExportAsPackChanged(bool value)
    { OnPropertyChanged(nameof(ExportSeparately)); OnPropertyChanged(nameof(ExportLabel)); OnPropertyChanged(nameof(OutputDescription)); RefreshSummary(); }
    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRun));
        AddFilesCommand.NotifyCanExecuteChanged(); AddFolderCommand.NotifyCanExecuteChanged();
        RemoveInputCommand.NotifyCanExecuteChanged(); ClearCommand.NotifyCanExecuteChanged(); AnalyzeCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged(); PickDestinationCommand.NotifyCanExecuteChanged();
        PickPackageDestinationCommand.NotifyCanExecuteChanged(); UseDefaultDestinationCommand.NotifyCanExecuteChanged();
        SelectAllCommand.NotifyCanExecuteChanged(); SelectNoneCommand.NotifyCanExecuteChanged(); ResetSoundCommand.NotifyCanExecuteChanged(); ApproveRepairCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged(); OpenPackCommand.NotifyCanExecuteChanged(); RecheckPackCommand.NotifyCanExecuteChanged(); SaveAsNewPackCommand.NotifyCanExecuteChanged();
        ApplyBulkSoundCommand.NotifyCanExecuteChanged(); RestoreBulkSoundCommand.NotifyCanExecuteChanged(); PickBulkDestinationCommand.NotifyCanExecuteChanged(); IncludeHighlightedCommand.NotifyCanExecuteChanged(); ExcludeHighlightedCommand.NotifyCanExecuteChanged();
    }
    private void RefreshSummary()
    {
        foreach (var property in new[] { nameof(PackageCount), nameof(VehicleCount), nameof(CollectionSize), nameof(HasPackages),
            nameof(NoPackages), nameof(CollectionSummary), nameof(TableSelection), nameof(SelectionSummary), nameof(InputSummary), nameof(SelectedTarget), nameof(CanExport), nameof(ExportNameError), nameof(HasExportNameError), nameof(ExportHint) }) OnPropertyChanged(property);
        AnalyzeCommand.NotifyCanExecuteChanged(); ExportCommand.NotifyCanExecuteChanged();
    }
    private void PackageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PackageViewModel.ResourceName)) PackagesView.Refresh();
        RefreshSummary();
        if (e.PropertyName is nameof(PackageViewModel.IsSelected) or nameof(PackageViewModel.ResourceName) or nameof(PackageViewModel.Package)) QueueValidation();
    }
    public void AddInputs(IEnumerable<string> paths)
    {
        if (IsBusy) return;
        var incoming = paths.ToArray(); RememberImport(incoming);
        foreach (var path in incoming)
            if (!Inputs.Contains(path, StringComparer.OrdinalIgnoreCase)) { Inputs.Add(path); HasPendingInputs = true; }
        RefreshSummary(); Status = $"{Inputs.Count} entrada(s). Analise para atualizar a coleção.";
    }
    [RelayCommand(CanExecute = nameof(CanRun))] private void AddFiles() => AddInputs(dialogs.PickInputs());
    [RelayCommand(CanExecute = nameof(CanRun))] private void AddFolder()
    { if (dialogs.PickFolder("Selecione uma pasta de DLCs/RPFs", FolderPurpose.Import) is { } folder) AddInputs([folder]); }
    [RelayCommand(CanExecute = nameof(CanRun))] private void RemoveInput(string? path)
    { if (path is not null && Inputs.Remove(path)) { HasPendingInputs = true; RefreshSummary(); Status = "Entradas alteradas. Analise novamente."; } }
    private void ReleasePackages()
    {
        ReleaseFeatureState(); SelectedPackage = null;
        foreach (var package in Packages) package.PropertyChanged -= PackageChanged;
        Packages.Clear(); session?.Dispose(); session = null;
    }
    [RelayCommand(CanExecute = nameof(CanRun))] private void Clear()
    {
        ReleasePackages(); Inputs.Clear(); Messages.Clear(); HasPendingInputs = false; SearchText = ""; LastEnsureCommand = "";
        ShowActivity = false; RefreshSummary(); Status = "Adicione mods ou uma pasta de resources para começar.";
    }
    [RelayCommand(CanExecute = nameof(CanRun))] private void PickDestination()
    { if (dialogs.PickFolder("Escolha a pasta padrão de saída") is { } folder) DestinationDirectory = folder; }
    [RelayCommand(CanExecute = nameof(CanRun))] private void PickPackageDestination(PackageViewModel? package)
    {
        package ??= SelectedPackage;
        if (package is not null && dialogs.PickFolder("Destino do pacote " + package.ResourceName) is { } folder) { package.DestinationDirectory = folder; RememberExport(folder); }
    }
    [RelayCommand(CanExecute = nameof(CanRun))] private void UseDefaultDestination(PackageViewModel? package)
    { if ((package ?? SelectedPackage) is { } item) item.DestinationDirectory = ""; }
    [RelayCommand(CanExecute = nameof(CanRun))] private void SelectAll()
    { foreach (var package in Packages.Where(p => p.Package.CanExport)) package.IsSelected = true; }
    [RelayCommand(CanExecute = nameof(CanRun))] private void SelectNone()
    { foreach (var package in Packages) package.IsSelected = false; }
    [RelayCommand(CanExecute = nameof(CanRun))] private void ResetSound(VehicleViewModel? vehicle)
    { if (vehicle is not null) vehicle.AudioNameHash = vehicle.OriginalAudio; }
    [RelayCommand(CanExecute = nameof(CanRun))] private void ApproveRepair(PackageViewModel? package)
    {
        package ??= SelectedPackage;
        if (package is null || !package.CanRepair) return;
        var description = package.RepairDescription;
        package.ApplyApprovedRepair();
        Messages.Add("Correção aprovada em " + package.ResourceName + ": " + description);
        RefreshSummary(); Status = package.ResourceName + ": cópia corrigida pronta para exportar.";
    }
    [RelayCommand] private void ToggleActivity() => ShowActivity = !ShowActivity;
    [RelayCommand(CanExecute = nameof(CanCopyEnsure))] private void CopyEnsure() => Clipboard.SetText(LastEnsureCommand);
    private IProgress<ConversionProgress> BeginOperation(string message)
    {
        cancellation = new CancellationTokenSource(); IsBusy = true; IsIndeterminate = true; ProgressValue = 0; Status = message;
        var operation = cancellation;
        return new Progress<ConversionProgress>(p =>
        {
            if (!IsBusy || cancellation != operation) return;
            Status = p.Message; IsIndeterminate = p.Total == 0;
            ProgressValue = p.Total == 0 ? 0 : 100d * p.Completed / p.Total;
        });
    }
    private void EndOperation() { IsBusy = false; IsIndeterminate = false; cancellation?.Dispose(); cancellation = null; }
    [RelayCommand(CanExecute = nameof(CanAnalyze))] private async Task AnalyzeAsync()
    {
        var progress = BeginOperation("Analisando a coleção…");
        try
        {
            var append = IsEditingPack;
            var inputs = append ? Inputs.Where(p => !analyzedEditInputs.Contains(p)).ToArray() : Inputs.ToArray();
            var saved = Packages.GroupBy(p => p.Package.SourceName,StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key,g => g.First(),StringComparer.OrdinalIgnoreCase);
            var next = await converter.AnalyzeAsync(inputs, progress, cancellation!.Token);
            if (append) additionalSessions.Add(next);
            else { ReleasePackages(); Messages.Clear(); session = next; LastEnsureCommand = ""; }
            var names = Packages.Select(p=>p.OutputResourceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var selectedModels = Packages.Where(p=>p.IsSelected).SelectMany(p=>p.Vehicles).Select(v=>v.ModelName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var package in next.Packages)
            {
                if (append && Packages.Any(p=>p.Package.SourceName==package.SourceName)) continue;
                var item = new PackageViewModel(package); var suffix = 2;
                if (!append && saved.TryGetValue(package.SourceName,out var previous))
                {
                    item.ResourceName=previous.ResourceName;item.DestinationDirectory=previous.DestinationDirectory;
                    item.IsSelected=previous.IsSelected&&package.CanExport;
                    foreach(var vehicle in item.Vehicles)
                        if(previous.Vehicles.FirstOrDefault(v=>v.Definition.Key==vehicle.Definition.Key) is { } old)vehicle.AudioNameHash=old.AudioNameHash;
                }
                var baseName = item.OutputResourceName;
                while (!names.Add(item.OutputResourceName)) item.ResourceName = baseName[..Math.Min(58, baseName.Length)] + "_" + suffix++;
                if (item.IsSelected && package.Vehicles.Any(v => selectedModels.Contains(v.ModelName)))
                {
                    item.IsSelected = false;
                    Messages.Add("Alternativa desmarcada para evitar modelo repetido: " + item.ResourceName + ". Escolha uma versão por modelo.");
                }
                if (item.IsSelected) foreach (var vehicle in package.Vehicles) selectedModels.Add(vehicle.ModelName);
                item.PropertyChanged += PackageChanged; Packages.Add(item);
            }
            if(append)foreach(var path in inputs)analyzedEditInputs.Add(path);
            foreach (var diagnostic in next.Diagnostics) Messages.Add(FormatDiagnostic(diagnostic));
            foreach (var blocked in Packages.Where(p => !p.Package.CanExport))
                Messages.Add("Bloqueado: " + blocked.ResourceName + ". " + blocked.BlockReason);
            ShowActivity = next.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error) || Packages.Any(p => !p.Package.CanExport);
            SearchText = ""; SelectedPackage = Packages.FirstOrDefault(p => !p.Package.CanExport) ?? Packages.FirstOrDefault(); HasPendingInputs = false;
            RefreshSummary(); await ValidateNowAsync();
            Status = Packages.Count == 0 ? "Nenhum DLC/RPF encontrado. Versões FiveM ready são ignoradas; selecione o Add-On ou abra um pack do aplicativo." : $"Coleção analisada: {Packages.Count} pacote(s), {Packages.Count(p => p.Package.CanExport)} pronto(s).";
        }
        catch (OperationCanceledException) { Status = "Análise cancelada."; }
        catch (Exception ex) { Messages.Add("Erro: " + ex.Message); ShowActivity = true; Status = "Não foi possível analisar as entradas."; }
        finally { EndOperation(); }
    }
    [RelayCommand(CanExecute = nameof(CanExport))] private async Task ExportAsync()
    {
        var selected = Packages.Where(p => p.IsSelected && p.Package.CanExport).ToArray();
        if (selected.Length == 0) { Status = "Selecione pelo menos um pacote pronto."; return; }
        if (ExportNameError.Length > 0) { Status = ExportNameError; return; }
        if (ExportAsPack)
        {
            BeginOperation("Conferindo a seleção antes de exportar…");
            try { await ValidateNowAsync(); if(cancellation!.IsCancellationRequested) { Status="Verificação cancelada. Nenhum arquivo exportado."; return; } } finally { EndOperation(); }
            if (HasPackConflicts) { Status=PackValidationSummary; Messages.Add(PackValidationSummary+"\n"+string.Join("\n",PackValidationIssues)); ShowActivity=true; return; }
        }
        foreach (var package in selected) package.ResourceName = package.OutputResourceName;
        if (ExportAsPack) PackResourceName = OutputPackName;
        if (string.IsNullOrWhiteSpace(DestinationDirectory) && (ExportAsPack || selected.Any(p => string.IsNullOrWhiteSpace(p.DestinationDirectory)))) PickDestination();
        if (string.IsNullOrWhiteSpace(DestinationDirectory) && (ExportAsPack || selected.Any(p => string.IsNullOrWhiteSpace(p.DestinationDirectory)))) return;
        var requests = selected.Select(p => new PackageExportRequest(p.Package, p.OutputResourceName,
            p.Vehicles.ToDictionary(v => v.Definition.Key, v => v.AudioNameHash))).ToArray();
        var destinations = selected.Select(p => (string.IsNullOrWhiteSpace(p.DestinationDirectory) ? DestinationDirectory : p.DestinationDirectory).Trim()).ToArray();
        var asPack = ExportAsPack; var packName = OutputPackName; var packDestination = DestinationDirectory.Trim();
        var progress = BeginOperation("Preparando a exportação…"); var succeeded = 0;
        try
        {
            if (asPack)
            {
                if (editingDocument is { } document)
                {
                    if(packName!=document.ResourceName || !Path.GetFullPath(packDestination).TrimEnd('\\','/').Equals(Path.GetDirectoryName(document.Directory),StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Use Salvar como novo para mudar o nome ou destino do pack aberto.");
                    var updated=await converter.UpdatePackAsync(document,requests,progress,cancellation!.Token);
                    RecordExport(updated.Export);
                    OpenPackResult reopened;
                    try { reopened=await converter.OpenPackAsync(updated.Document.Directory,progress,cancellation.Token); }
                    catch(Exception reload)
                    {
                        HasPendingInputs=true;editingDocument=updated.Document;RefreshPackMode();
                        Status="Pack salvo. Reabra-o para continuar a edição.";
                        Messages.Add("O pack foi salvo, mas a atualização da lista não terminou: "+reload.Message);ShowActivity=true;return;
                    }
                    ReleasePackages();session=reopened.Session;editingDocument=reopened.Document;Inputs.Clear();
                    foreach(var package in reopened.Session.Packages)
                    {var item=new PackageViewModel(package){LastExportPath=updated.Document.Directory};item.PropertyChanged+=PackageChanged;Packages.Add(item);}
                    HasPendingInputs=false;SelectedPackage=Packages.FirstOrDefault();RefreshPackMode();RefreshSummary();await ValidateNowAsync();
                    Status=$"Pack atualizado: {Packages.Count} pacote(s).";ProgressValue=100;return;
                }
                var result = await converter.ExportPackAsync(requests, new(packName, packDestination), progress, cancellation!.Token);
                RecordExport(result); foreach (var package in selected) package.LastExportPath = result.ResourceDirectory;
                Status = $"Pack {packName} exportado com {selected.Length} pacote(s)."; ProgressValue = 100; return;
            }
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < requests.Length; i++)
                if (!targets.Add(Path.GetFullPath(Path.Combine(destinations[i], requests[i].ResourceName))))
                    throw new InvalidDataException("Dois pacotes usam o mesmo destino e nome. Ajuste os destinos individuais antes de exportar.");
            for (var i = 0; i < requests.Length; i++)
            {
                cancellation!.Token.ThrowIfCancellationRequested();
                try
                {
                    var request = requests[i];
                    var result = await converter.ExportAsync(request.Package, new(request.ResourceName, destinations[i], request.AudioOverrides), progress, cancellation.Token);
                    RecordExport(result); selected[i].LastExportPath = result.ResourceDirectory; succeeded++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { Messages.Add($"Erro em {requests[i].ResourceName}: {ex.Message}"); ShowActivity = true; }
            }
            Status = $"Exportação concluída: {succeeded} de {selected.Length} pacotes. Consulte Atividade."; ProgressValue = 100;
        }
        catch (OperationCanceledException) { Status = $"Exportação cancelada. {succeeded} resource(s) separado(s) já concluído(s)."; }
        catch (Exception ex) { Messages.Add("Erro: " + ex.Message); ShowActivity = true; Status = "Exportação interrompida: " + ex.Message; }
        finally { EndOperation(); }
    }
    private void RecordExport(ExportResult result)
    { Messages.Add("Exportado: " + result.ResourceDirectory); Messages.Add(result.EnsureCommand); LastEnsureCommand = result.EnsureCommand; }
    [RelayCommand(CanExecute = nameof(IsBusy))] private void Cancel() { cancellation?.Cancel(); validationCancellation?.Cancel(); Status = "Cancelando…"; }
    public static string FormatDiagnostic(Diagnostic diagnostic) => (diagnostic.Severity switch
    { DiagnosticSeverity.Error => "Erro", DiagnosticSeverity.Warning => "Aviso", _ => "Info" }) + ": " + diagnostic.Message;
    public void Dispose() { cancellation?.Cancel(); cancellation?.Dispose(); ReleasePackages(); }
}
