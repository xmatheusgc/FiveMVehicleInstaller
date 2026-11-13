using FiveMVehicleInstaller.Core;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace FiveMVehicleInstaller.Infrastructure;

internal sealed record VirtualFile(string Path, IContentSource? Content, string? Error = null, bool InRpf = false, bool IsManaged = false, bool IsContainer = false, IReadOnlyList<ModCredit>? Credits = null);

internal sealed class PackageScanner(IRpfReader rpfReader, string workspace,
    IProgress<ConversionProgress>? progress, CancellationToken cancellationToken)
{
    private int fileCount;
    private long extractedBytes;
    private const long MaxExtractedBytes = 32L * 1024 * 1024 * 1024;

    public async Task<List<VirtualFile>> ScanAsync(string input)
    {
        SafePaths.RejectReparsePoints(input);
        var files = new List<VirtualFile>();
        if (Directory.Exists(input))
        {
            foreach (var file in EnumerateDirectory(input))
            {
                var relative = SafePaths.NormalizeRelative(Path.GetRelativePath(input, file));
                await ExpandAsync(relative, new FileContentSource(file), files, 0);
            }
        }
        else if (File.Exists(input))
            await ExpandAsync(SafePaths.NormalizeRelative(Path.GetFileName(input)), new FileContentSource(input), files, 0);
        else throw new FileNotFoundException("Entrada não encontrada.", input);
        return files;
    }

    private IEnumerable<string> EnumerateDirectory(string directory)
    {
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.TryPop(out var current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var entry in Directory.EnumerateFileSystemEntries(current).Order(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"Link ou junção não suportado: {entry}");
                if (Directory.Exists(entry)) pending.Push(entry);
                else yield return entry;
            }
        }
    }

    private async Task ExpandAsync(string path, IContentSource source, List<VirtualFile> files, int depth, bool insideRpf = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (++fileCount > 100_000 || depth > 16) throw new InvalidDataException("Limite de arquivos ou de arquivos aninhados excedido.");
        var extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".rpf" or ".zip" or ".rar" or ".7z"))
        {
            files.Add(new(path, source, InRpf: insideRpf));
            return;
        }
        progress?.Report(new($"Lendo {path}"));
        var firstFile = files.Count;
        try
        {
            string containerPath;
            if (source is FileContentSource local) containerPath = local.FilePath;
            else
            {
                containerPath = SafePaths.Within(workspace, Guid.NewGuid().ToString("N") + extension);
                await using var sourceStream = await source.OpenReadAsync(cancellationToken);
                await using var target = new FileStream(containerPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await CopyLimitedAsync(sourceStream, target, source.Length);
            }
            if (extension == ".rpf")
            {
                var entries = rpfReader.Read(containerPath);
                // Retain the existence of a successfully parsed archive, even if
                // it has no entries. DLC references point to containers too.
                files.Add(new(path, source, InRpf: insideRpf, IsContainer: true));
                foreach (var entry in entries)
                    await ExpandAsync(path + "/" + entry.Path, entry.Content, files, depth + 1, insideRpf: true);
                return;
            }
            using var archive = ArchiveFactory.Open(containerPath);
            var extracted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            async Task ExtractEntryAsync(IEntry entry, Func<Stream> open)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entryName = SafePaths.NormalizeRelative((entry.Key ?? "").TrimEnd('/', '\\'));
                var attributes = entry.Attrib.GetValueOrDefault();
                if (entry.LinkTarget is not null || entry.IsEncrypted || (attributes & 0x400) != 0 ||
                    ((attributes >> 16) & 0xF000) == 0xA000)
                    throw new NotSupportedException("Links e compactados com senha não são suportados.");
                if (!extracted.Add(entryName)) throw new InvalidDataException("Compactado contém caminhos duplicados.");
                if (entry.IsDirectory) return;
                var targetPath = SafePaths.Within(workspace, Guid.NewGuid().ToString("N") + System.IO.Path.GetExtension(entryName));
                using (var entryStream = open())
                await using (var target = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    await CopyLimitedAsync(entryStream, target, entry.Size, synchronousReads: true);
                await ExpandAsync(path + "/" + entryName, new FileContentSource(targetPath), files, depth + 1, insideRpf);
            }
            if (archive.IsSolid || archive.Type == ArchiveType.SevenZip)
            {
                using var reader = archive.ExtractAllEntries();
                while (reader.MoveToNextEntry()) await ExtractEntryAsync(reader.Entry, () => reader.OpenEntryStream());
            }
            else
                foreach (var entry in archive.Entries) await ExtractEntryAsync(entry, () => entry.OpenEntryStream());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An invalid container must never leave a partially discovered, exportable DLC.
            files.RemoveRange(firstFile, files.Count - firstFile);
            files.Add(new(path, null, $"{path}: {ex.Message}"));
        }
    }

    private async Task CopyLimitedAsync(Stream source, Stream destination, long expected, bool synchronousReads = false)
    {
        if (expected < 0 || expected > MaxExtractedBytes - extractedBytes)
            throw new InvalidDataException("Limite de 32 GiB de extração temporária excedido.");
        var buffer = new byte[81920];
        long copied = 0;
        int count;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // SharpCompress 1.0.0's async RAR VM filters corrupt this real-world DLC.
            // Sync decompression runs on the converter's worker task, never the WPF dispatcher.
            count = synchronousReads ? source.Read(buffer, 0, buffer.Length) : await source.ReadAsync(buffer, cancellationToken);
            if (count == 0) break;
            copied += count;
            extractedBytes += count;
            if (copied > expected || extractedBytes > MaxExtractedBytes)
                throw new InvalidDataException("Tamanho descompactado inválido ou limite de extração excedido.");
            await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
        if (copied != expected) throw new InvalidDataException("Arquivo compactado truncado.");
    }
}
