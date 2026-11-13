namespace FiveMVehicleInstaller.Core;

public enum DiagnosticSeverity { Information, Warning, Error }

public sealed record Diagnostic(DiagnosticSeverity Severity, string Message);
public sealed record ConversionProgress(string Message, int Completed = 0, int Total = 0);

/// <summary>Returns an independent readable stream, disposed by the caller.</summary>
public interface IContentSource
{
    long Length { get; }
    ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default);
}

public sealed record PackageFile(string SourcePath, string DestinationPath, string? DataFileType, IContentSource Content);

// The key distinguishes equal model names in different metadata documents/items.
public sealed record VehicleDefinition(string Key, string MetadataPath, int ItemIndex,
    string ModelName, string TextureName, string HandlingId, string AudioNameHash);

public sealed record PackageRepairProposal(string Description, VehiclePackage RepairedPackage);

public enum CreditConfidence { Explicit, NameHint }
public sealed record ModCredit(string Name, string Role, string SourcePath, string Evidence, CreditConfidence Confidence);

public sealed class VehiclePackage
{
    public required string SourceName { get; init; }
    public bool IsFromPack { get; init; }
    public IReadOnlyList<ModCredit> Credits { get; init; } = [];
    public PackageRepairProposal? SuggestedRepair { get; init; }
    public required string SuggestedResourceName { get; init; }
    public required IReadOnlyList<VehicleDefinition> Vehicles { get; init; }
    public required IReadOnlyList<PackageFile> Files { get; init; }
    public required IReadOnlyList<Diagnostic> Diagnostics { get; init; }
    public bool CanExport => Vehicles.Count > 0 && !Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
}

/// <summary>Owns extracted containers. Keep alive until all package exports have finished.</summary>
public sealed class AnalysisSession(IReadOnlyList<VehiclePackage> packages,
    IReadOnlyList<Diagnostic> diagnostics, Action cleanup) : IDisposable
{
    private int disposed;
    public IReadOnlyList<VehiclePackage> Packages { get; } = packages;
    public IReadOnlyList<Diagnostic> Diagnostics { get; } = diagnostics;
    public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) == 0) cleanup(); }
}

public sealed record ExportOptions(string ResourceName, string DestinationDirectory,
    IReadOnlyDictionary<string, string> AudioOverrides);
public sealed record PackageExportRequest(VehiclePackage Package, string ResourceName,
    IReadOnlyDictionary<string, string> AudioOverrides);
public sealed record PackExportOptions(string ResourceName, string DestinationDirectory);

public sealed record PackIssue(string Message, IReadOnlyList<string> Packages);
public sealed record PackValidationResult(IReadOnlyList<PackIssue> Issues)
{
    public bool CanExport => Issues.Count == 0;
}
public sealed record PackDocument(string Directory, string ResourceName, string Revision);
public sealed record OpenPackResult(PackDocument Document, AnalysisSession Session);
public sealed record PackUpdateResult(PackDocument Document, ExportResult Export);

public sealed record ExportResult(string ResourceDirectory, string EnsureCommand, int FileCount);

public interface IVehicleConverter
{
    Task<PackValidationResult> ValidatePackAsync(IReadOnlyList<PackageExportRequest> packages, CancellationToken cancellationToken = default);
    Task<OpenPackResult> OpenPackAsync(string directory, IProgress<ConversionProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<PackUpdateResult> UpdatePackAsync(PackDocument document, IReadOnlyList<PackageExportRequest> packages,
        IProgress<ConversionProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<AnalysisSession> AnalyzeAsync(IEnumerable<string> inputs,
        IProgress<ConversionProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<ExportResult> ExportPackAsync(IReadOnlyList<PackageExportRequest> packages, PackExportOptions options,
        IProgress<ConversionProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<ExportResult> ExportAsync(VehiclePackage package, ExportOptions options,
        IProgress<ConversionProgress>? progress = null, CancellationToken cancellationToken = default);
}

public interface IRpfReader
{
    IReadOnlyList<RpfFileEntry> Read(string archivePath);
}

public sealed record RpfFileEntry(string Path, IContentSource Content);
