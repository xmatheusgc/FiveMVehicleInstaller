using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using FiveMVehicleInstaller.Core;

namespace FiveMVehicleInstaller.Infrastructure;

internal sealed record PreparedMetadata(XDocument Document, IContentSource Content, int RemovedPaddingBytes);

// Some DLC metadata has NUL terminators/padding after the XML document. This is
// a transport detail; never remove characters inside the document or fix tags here.
internal static class MetadataInput
{
    public static async Task<PreparedMetadata> ReadAsync(IContentSource source, CancellationToken ct)
    {
        try { return new(await Metadata.ReadAsync(source, ct), source, 0); }
        catch (XmlException)
        {
            var trimmed = await TryTrimPaddingAsync(source, ct);
            if (trimmed is not null) return trimmed;
            throw; // Preserve the original diagnostic for malformed/truncated XML.
        }
    }

    private static async Task<PreparedMetadata?> TryTrimPaddingAsync(IContentSource source, CancellationToken ct)
    {
        if (source.Length > 16 * 1024 * 1024) return null;
        await using var input = await source.OpenReadAsync(ct);
        using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        if (bytes.Length == 0 || bytes.Length > 16 * 1024 * 1024) return null;
        try
        {
            // ASCII-compatible declarations cover UTF-8 and single-byte encodings;
            // BOMs and UTF-16/32 signatures take precedence in DetectEncoding.
            var header = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 1024));
            var declaration = Regex.Match(header,
                "\\A<\\?xml\\s[^?]*?encoding\\s*=\\s*['\"](?<encoding>[^'\"]+)['\"]",
                RegexOptions.CultureInvariant);
            var (encoding, preamble) = XmlBytePatcher.DetectEncoding(bytes,
                declaration.Success ? declaration.Groups["encoding"].Value : null);
            var text = encoding.GetString(bytes, preamble, bytes.Length - preamble);
            if (!encoding.GetBytes(text).AsSpan().SequenceEqual(bytes.AsSpan(preamble))) return null;
            var end = text.Length;
            while (end > 0 && text[end - 1] == '\0') end--;
            if (end == text.Length || end == 0) return null;
            var paddingBytes = encoding.GetByteCount(text.AsSpan(end));
            var content = new MemoryContentSource(bytes[..^paddingBytes]);
            // Strict, complete parsing still rejects internal NULs, broken tags,
            // trailing garbage, multiple roots and incomplete documents.
            var document = await Metadata.ReadAsync(content, ct);
            if (document.Root is null) return null;
            return new(document, content, paddingBytes);
        }
        catch (Exception ex) when (ex is XmlException or ArgumentException or DecoderFallbackException or NotSupportedException)
        { return null; }
    }
}