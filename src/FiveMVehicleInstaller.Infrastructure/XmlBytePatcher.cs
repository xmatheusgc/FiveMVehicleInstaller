using System.Security;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace FiveMVehicleInstaller.Infrastructure;

// XML is parsed only to identify the selected Item. Edits are spliced into the original bytes.
internal sealed class XmlBytePatcher
{
    private sealed record Edit(int Start, int Length, string Replacement);
    private readonly byte[] original;
    private readonly Encoding encoding;
    private readonly int preambleLength;
    private readonly string text;
    private readonly List<int> lineStarts = [0];
    private readonly List<Edit> edits = [];

    public XmlBytePatcher(byte[] original, string? declaredEncoding)
    {
        this.original = original;
        (encoding, preambleLength) = DetectEncoding(original, declaredEncoding);
        text = encoding.GetString(original, preambleLength, original.Length - preambleLength);
        if (!encoding.GetBytes(text).AsSpan().SequenceEqual(original.AsSpan(preambleLength)))
            throw new InvalidDataException("Não foi possível preservar a codificação original do XML.");
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                lineStarts.Add(i + 1);
            }
            else if (text[i] == '\n') lineStarts.Add(i + 1);
        }
    }

    public void ReplaceValue(XElement element, string value)
    {
        if (element.HasElements) throw new InvalidDataException("O campo XML deve conter apenas texto.");
        var (_, openingEnd, closingStart, name) = Locate(element);
        var escaped = SecurityElement.Escape(value) ?? "";
        if (closingStart < 0)
            edits.Add(new(openingEnd - 1, 2, ">" + escaped + "</" + name + ">"));
        else
        {
            var contentStart = openingEnd + 1;
            var contentEnd = closingStart;
            // Keep padding around a plain text identifier, including tabs and line endings.
            if (!text.AsSpan(contentStart, contentEnd - contentStart).Contains('<'))
            {
                while (contentStart < contentEnd && char.IsWhiteSpace(text[contentStart])) contentStart++;
                while (contentEnd > contentStart && char.IsWhiteSpace(text[contentEnd - 1])) contentEnd--;
            }
            edits.Add(new(contentStart, contentEnd - contentStart, escaped));
        }
    }

    public void ReplaceAttribute(XAttribute attribute, string value)
    {
        var info = (IXmlLineInfo)attribute;
        if (!info.HasLineInfo()) throw new InvalidDataException("Posição do atributo XML indisponível.");
        var start = lineStarts[info.LineNumber - 1] + info.LinePosition - 1;
        var position = start;
        while (position < text.Length && !char.IsWhiteSpace(text[position]) && text[position] != '=') position++;
        while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
        if (position >= text.Length || text[position++] != '=') throw new InvalidDataException("Atributo XML não localizado.");
        while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
        if (position >= text.Length || text[position] is not ((char)39 or (char)34)) throw new InvalidDataException("Aspas do atributo XML não localizadas.");
        var quote = text[position++];
        var end = text.IndexOf(quote, position);
        if (end < 0) throw new InvalidDataException("Atributo XML truncado.");
        while (position < end && char.IsWhiteSpace(text[position])) position++;
        while (end > position && char.IsWhiteSpace(text[end - 1])) end--;
        edits.Add(new(position, end - position, SecurityElement.Escape(value) ?? ""));
    }
    public void InsertChild(XElement parent, string localName, string value)
    {
        var (_, _, closingStart, name) = Locate(parent);
        if (closingStart < 0) throw new InvalidDataException("Item do veículo sem conteúdo.");
        var separator = name.IndexOf(':');
        var qualifiedName = (separator >= 0 ? name[..(separator + 1)] : "") + localName;
        edits.Add(new(closingStart, 0, "<" + qualifiedName + ">" + SecurityElement.Escape(value) + "</" + qualifiedName + ">"));
    }

    private (int Start, int OpeningEnd, int ClosingStart, string Name) Locate(XElement element)
    {
        var info = (IXmlLineInfo)element;
        if (!info.HasLineInfo()) throw new InvalidDataException("Posição do campo XML indisponível.");
        var start = lineStarts[info.LineNumber - 1] + info.LinePosition - 2;
        if (start < 0 || start >= text.Length || text[start] != '<')
            throw new InvalidDataException("Não foi possível localizar o campo XML original.");
        var nameEnd = start + 1;
        while (nameEnd < text.Length && !char.IsWhiteSpace(text[nameEnd]) && text[nameEnd] is not ('/' or '>')) nameEnd++;
        var name = text[(start + 1)..nameEnd];
        var openingEnd = TagEnd(nameEnd);
        if (text[openingEnd - 1] == '/') return (start, openingEnd, -1, name);
        var depth = 1;
        for (var position = openingEnd + 1; position < text.Length;)
        {
            position = text.IndexOf('<', position);
            if (position < 0) break;
            if (text.AsSpan(position).StartsWith("<!--")) { position = After(position + 4, "-->"); continue; }
            if (text.AsSpan(position).StartsWith("<![CDATA[")) { position = After(position + 9, "]]>"); continue; }
            if (text.AsSpan(position).StartsWith("<?")) { position = After(position + 2, "?>"); continue; }
            var end = TagEnd(position + 1);
            if (text[position + 1] == '/')
            {
                if (--depth == 0) return (start, openingEnd, position, name);
            }
            else if (text[end - 1] != '/') depth++;
            position = end + 1;
        }
        throw new InvalidDataException("Fim do campo XML não encontrado.");
    }

    private int After(int position, string terminator)
    {
        var end = text.IndexOf(terminator, position, StringComparison.Ordinal);
        return end < 0 ? throw new InvalidDataException("XML truncado.") : end + terminator.Length;
    }

    private int TagEnd(int position)
    {
        char quote = '\0';
        for (; position < text.Length; position++)
        {
            var c = text[position];
            if (quote != '\0') { if (c == quote) quote = '\0'; }
            else if (c is '\'' or '"') quote = c;
            else if (c == '>') return position;
        }
        throw new InvalidDataException("Tag XML truncada.");
    }

    public async Task WriteAsync(Stream destination, CancellationToken cancellationToken)
    {
        var charPosition = 0;
        var bytePosition = preambleLength;
        await destination.WriteAsync(original.AsMemory(0, preambleLength), cancellationToken);
        foreach (var edit in edits.OrderBy(e => e.Start))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (edit.Start < charPosition) throw new InvalidDataException("Alterações XML sobrepostas.");
            var unchangedLength = encoding.GetByteCount(text.AsSpan(charPosition, edit.Start - charPosition));
            await destination.WriteAsync(original.AsMemory(bytePosition, unchangedLength), cancellationToken);
            bytePosition += unchangedLength + encoding.GetByteCount(text.AsSpan(edit.Start, edit.Length));
            charPosition = edit.Start + edit.Length;
            await destination.WriteAsync(encoding.GetBytes(edit.Replacement), cancellationToken);
        }
        await destination.WriteAsync(original.AsMemory(bytePosition), cancellationToken);
    }

    internal static (Encoding Encoding, int PreambleLength) DetectEncoding(byte[] bytes, string? declared)
    {
        var data = bytes.AsSpan();
        if (data.StartsWith(new byte[] { 0xFF, 0xFE, 0, 0 })) return (new UTF32Encoding(false, false, true), 4);
        if (data.StartsWith(new byte[] { 0, 0, 0xFE, 0xFF })) return (new UTF32Encoding(true, false, true), 4);
        if (data.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) return (new UTF8Encoding(false, true), 3);
        if (data.StartsWith(new byte[] { 0xFF, 0xFE })) return (new UnicodeEncoding(false, false, true), 2);
        if (data.StartsWith(new byte[] { 0xFE, 0xFF })) return (new UnicodeEncoding(true, false, true), 2);
        if (data.StartsWith(new byte[] { 0x3C, 0, 0, 0 })) return (new UTF32Encoding(false, false, true), 0);
        if (data.StartsWith(new byte[] { 0, 0, 0, 0x3C })) return (new UTF32Encoding(true, false, true), 0);
        if (data.StartsWith(new byte[] { 0x3C, 0, 0x3F, 0 })) return (new UnicodeEncoding(false, false, true), 0);
        if (data.StartsWith(new byte[] { 0, 0x3C, 0, 0x3F })) return (new UnicodeEncoding(true, false, true), 0);
        return (Encoding.GetEncoding(declared ?? "UTF-8", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback), 0);
    }
}
