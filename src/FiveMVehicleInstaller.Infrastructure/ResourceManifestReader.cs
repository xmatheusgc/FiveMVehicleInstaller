using System.Text;
using System.Text.RegularExpressions;
using FiveMVehicleInstaller.Core;

namespace FiveMVehicleInstaller.Infrastructure;

internal sealed record ManifestInspection(IReadOnlyDictionary<string, string> Metadata, IReadOnlyList<Diagnostic> Diagnostics);

// Parse a restricted declarative manifest. Never execute user-supplied Lua.
internal static class ResourceManifestReader
{
    public static async Task<ManifestInspection> InspectAsync(VirtualFile manifest, string root,
        IReadOnlyList<VirtualFile> files, CancellationToken cancellationToken)
    {
        var diagnostics = new List<Diagnostic>();
        var references = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (manifest.Content!.Length > 1024 * 1024) throw new InvalidDataException("Manifest excede 1 MiB.");
        await using var stream = await manifest.Content.OpenReadAsync(cancellationToken);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
        var tokens = Tokenize(await reader.ReadToEndAsync(cancellationToken));
        var fileGlobs = new List<string>();
        var declarations = new List<(string Type, string Glob)>();
        var position = 0;
        string Take()
        {
            if (position >= tokens.Count) throw new InvalidDataException("Manifest incompleto.");
            return tokens[position++];
        }
        string Literal()
        {
            var token = Take();
            if (!token.StartsWith('\0')) throw new InvalidDataException("Manifest dinâmico não suportado. Use caminhos literais.");
            return token[1..];
        }
        while (position < tokens.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directive = Take();
            if (directive == ";") continue;
            if (directive is not ("fx_version" or "resource_manifest_version" or "game" or "games" or "author" or
                "description" or "version" or "url" or "lua54" or "files" or "file" or "data_file"))
                throw new InvalidDataException($"Resource com declaração não suportada: {directive}. Scripts, áudio, dependências e manifests dinâmicos precisam permanecer no resource original.");
            var parenthesized = position < tokens.Count && tokens[position] == "(";
            if (parenthesized) position++;
            var values = new List<string>();
            if (position < tokens.Count && tokens[position] == "{")
            {
                position++;
                while (position < tokens.Count && tokens[position] != "}")
                {
                    values.Add(Literal());
                    if (position < tokens.Count && tokens[position] is "," or ";") position++;
                    else if (position < tokens.Count && tokens[position] != "}") throw new InvalidDataException("Lista do manifest inválida.");
                }
                if (Take() != "}") throw new InvalidDataException("Lista do manifest incompleta.");
            }
            else
            {
                values.Add(Literal());
                if (directive == "data_file")
                {
                    if (position < tokens.Count && tokens[position] == ",") position++;
                    values.Add(Literal());
                }
            }
            if (parenthesized && Take() != ")") throw new InvalidDataException("Manifest dinâmico não suportado.");
            if (directive is "game" or "games" && (values.Count == 0 || values.Any(v => v != "gta5")))
                throw new InvalidDataException("Apenas resources de GTA V são suportados.");
            if (directive is "files" or "file") fileGlobs.AddRange(values.Select(NormalizeGlob));
            if (directive == "data_file")
            {
                if (values.Count == 2 && values[0] == "DLCTEXT_FILE")
                {
                    var path = NormalizeGlob(values[1]);
                    diagnostics.Add(new(DiagnosticSeverity.Warning, $"Metadado de idioma não exportado: DLCTEXT_FILE ({path}). Esta versão preserva os cinco tipos de metadados de veículos suportados."));
                    continue;
                }
                if (values.Count != 2 || !Metadata.Types.Values.Contains(values[0]))
                    throw new InvalidDataException("Tipo de data_file não suportado: " + string.Join(", ", values));
                declarations.Add((values[0], NormalizeGlob(values[1])));
            }
        }
        foreach (var (type, glob) in declarations)
        {
            var matched = files.Where(f => f.Content is not null && Matches(Relative(f.Path, root), glob)).ToArray();
            if (matched.Length == 0) diagnostics.Add(new(DiagnosticSeverity.Error, $"Manifest referencia arquivo ausente: {glob}"));
            foreach (var file in matched)
            {
                if (!fileGlobs.Any(g => Matches(Relative(file.Path, root), g)))
                    diagnostics.Add(new(DiagnosticSeverity.Error, $"Metadado não está em files no manifest: {Relative(file.Path, root)}"));
                if (references.TryGetValue(file.Path, out var previous) && previous != type)
                    diagnostics.Add(new(DiagnosticSeverity.Error, $"Tipos conflitantes no manifest: {Relative(file.Path, root)}"));
                references[file.Path] = type;
            }
        }
        return new(references, diagnostics);
    }

    private static string Relative(string path, string root) => root.Length == 0 ? path : path[(root.Length + 1)..];
    private static string NormalizeGlob(string value)
    {
        var normalized = value.Replace('\\', '/');
        SafePaths.NormalizeRelative(normalized.Replace("*", "wildcard", StringComparison.Ordinal));
        return normalized;
    }
    private static bool Matches(string path, string glob)
    {
        var pattern = Regex.Escape(glob).Replace("\\*\\*/", "(?:.*/)?", StringComparison.Ordinal)
            .Replace("\\*\\*", ".*", StringComparison.Ordinal).Replace("\\*", "[^/]*", StringComparison.Ordinal);
        return Regex.IsMatch(path, "^" + pattern + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static List<string> Tokenize(string lua)
    {
        var tokens = new List<string>();
        for (var i = 0; i < lua.Length;)
        {
            if (char.IsWhiteSpace(lua[i])) { i++; continue; }
            if (lua.AsSpan(i).StartsWith("--"))
            {
                i += 2;
                if (i < lua.Length && lua[i] == '[')
                {
                    var endOpening = i + 1;
                    while (endOpening < lua.Length && lua[endOpening] == '=') endOpening++;
                    if (endOpening < lua.Length && lua[endOpening] == '[')
                    {
                        var terminator = "]" + lua[(i + 1)..endOpening] + "]";
                        var end = lua.IndexOf(terminator, endOpening + 1, StringComparison.Ordinal);
                        if (end < 0) throw new InvalidDataException("Comentário Lua incompleto.");
                        i = end + terminator.Length; continue;
                    }
                }
                while (i < lua.Length && lua[i] is not ('\r' or '\n')) i++;
                continue;
            }
            if (lua[i] is '\'' or '"')
            {
                var quote = lua[i++]; var value = new StringBuilder(); var closed = false;
                while (i < lua.Length)
                {
                    var c = lua[i++];
                    if (c == quote) { closed = true; break; }
                    if (c is '\r' or '\n') throw new InvalidDataException("String Lua incompleta.");
                    if (c == '\\')
                    {
                        if (i >= lua.Length) break;
                        c = lua[i++] switch
                        {
                            'n' => '\n', 'r' => '\r', 't' => '\t', '\\' => '\\', '\'' => '\'', '"' => '"',
                            _ => throw new InvalidDataException("Escape Lua não suportado no manifest.")
                        };
                    }
                    value.Append(c);
                }
                if (!closed) throw new InvalidDataException("String Lua incompleta.");
                tokens.Add("\0" + value); continue;
            }
            if (char.IsLetter(lua[i]) || lua[i] == '_')
            {
                var start = i++;
                while (i < lua.Length && (char.IsLetterOrDigit(lua[i]) || lua[i] == '_')) i++;
                tokens.Add(lua[start..i]); continue;
            }
            if ("{}(),;".Contains(lua[i])) { tokens.Add(lua[i++].ToString()); continue; }
            throw new InvalidDataException("Manifest dinâmico ou sintaxe Lua não suportada.");
        }
        return tokens;
    }
}
