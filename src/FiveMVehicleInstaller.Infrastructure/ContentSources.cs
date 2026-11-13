using FiveMVehicleInstaller.Core;

namespace FiveMVehicleInstaller.Infrastructure;

public sealed class FileContentSource : IContentSource
{
    public string FilePath { get; }
    public long Length { get; }
    private readonly DateTime modified;

    public FileContentSource(string path)
    {
        FilePath = Path.GetFullPath(path);
        var info = new FileInfo(FilePath);
        Length = info.Length;
        modified = info.LastWriteTimeUtc;
    }

    public ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SafePaths.RejectReparsePoints(FilePath);
        var info = new FileInfo(FilePath);
        if (info.Length != Length || info.LastWriteTimeUtc != modified)
            throw new IOException($"O arquivo mudou depois da análise. Importe novamente: {FilePath}");
        return ValueTask.FromResult<Stream>(new FileStream(FilePath, FileMode.Open, FileAccess.Read,
            FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan));
    }
}

internal sealed class SliceStream(Stream inner, long start, long length) : Stream
{
    private long position;
    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => length;
    public override long Position { get => position; set => Seek(value, SeekOrigin.Begin); }
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer)
    {
        inner.Position = start + position;
        var read = inner.Read(buffer[..(int)Math.Min(buffer.Length, length - position)]);
        if (read == 0 && position < length) throw new EndOfStreamException("Arquivo truncado.");
        position += read;
        return read;
    }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        inner.Position = start + position;
        var read = await inner.ReadAsync(buffer[..(int)Math.Min(buffer.Length, length - position)], cancellationToken);
        if (read == 0 && position < length) throw new EndOfStreamException("Arquivo truncado.");
        position += read;
        return read;
    }
    public override long Seek(long offset, SeekOrigin origin)
    {
        var next = origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => position + offset,
            SeekOrigin.End => length + offset, _ => throw new ArgumentOutOfRangeException(nameof(origin)) };
        if (next < 0 || next > length) throw new IOException("Posição fora do arquivo.");
        return position = next;
    }
    protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

internal sealed class PrefixStream(byte[] prefix, Stream body) : Stream
{
    private int prefixPosition;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => prefix.Length + body.Length;
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer)
    {
        if (prefixPosition >= prefix.Length) return body.Read(buffer);
        var count = Math.Min(buffer.Length, prefix.Length - prefixPosition);
        prefix.AsSpan(prefixPosition, count).CopyTo(buffer);
        prefixPosition += count;
        return count;
    }
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return prefixPosition < prefix.Length ? ValueTask.FromResult(Read(buffer.Span)) : body.ReadAsync(buffer, cancellationToken);
    }
    protected override void Dispose(bool disposing) { if (disposing) body.Dispose(); base.Dispose(disposing); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

internal sealed class ExactLengthStream(Stream inner, long expected) : Stream
{
    private long readTotal;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => expected;
    public override long Position { get => readTotal; set => throw new NotSupportedException(); }
    private int Validate(int count)
    {
        readTotal += count;
        if (readTotal > expected || (count == 0 && readTotal != expected))
            throw new InvalidDataException("Tamanho descompactado diferente do índice RPF.");
        return count;
    }
    public override int Read(byte[] buffer, int offset, int count) => count == 0 ? 0 : Validate(inner.Read(buffer, offset, count));
    public override int Read(Span<byte> buffer) => buffer.IsEmpty ? 0 : Validate(inner.Read(buffer));
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        buffer.IsEmpty ? 0 : Validate(await inner.ReadAsync(buffer, cancellationToken));
    protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
