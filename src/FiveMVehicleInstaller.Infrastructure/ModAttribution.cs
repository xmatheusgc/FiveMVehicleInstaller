using System.Text;
using System.Text.RegularExpressions;
using FiveMVehicleInstaller.Core;

namespace FiveMVehicleInstaller.Infrastructure;

// Evidence is deliberately retained: a filename is a hint, and a converter may
// differ from the original model creator. Never infer authors from arbitrary links.
internal static class ModAttribution
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);
    private static readonly Regex ByName = new(@"(?:^|[\s_\-])by[\s_]+(?<name>.+)$", RegexOptions.IgnoreCase, Timeout);
    private static readonly Regex TitleBy = new(@"^(?:#{1,6}\s*)?(?:19|20)\d{2}\s+[^:]{2,100}?\s+by\s+(?<name>.+)$", RegexOptions.IgnoreCase, Timeout);
    private static readonly Regex VersionSuffix = new(@"\s+(?:v(?:ersion)?\s*)?\d+(?:[.\-]\d+)*[a-z]?(?:\s.*)?$|\s*\[(?:add[ -]?on|replace|fivem|tuning|template).*?$", RegexOptions.IgnoreCase, Timeout);
    private static readonly Regex CreditLine = new(
        @"^\s*(?:[-*#>]+\s*)?(?<role>author|authors|creator|created\s+by|mod\s+by|made\s+by|autor(?:es)?|criador(?:es)?|criado\s+por|convert(?:ed)?(?:\s+to\s+(?:gta\s*v|gta\s*5|gtav))?(?:\s*/\s*edit)?(?:\s+by)?|conversion(?:\s+by)?|convers[aã]o(?:\s+por)?|main\s+works?|other\s+work)\s*(?<separator>[:：.\-–=]\s*|\s+)(?<name>.+)$",
        RegexOptions.IgnoreCase, Timeout);

    public static async Task<IReadOnlyList<ModCredit>> DetectAsync(string input, string root,
        IReadOnlyCollection<string> roots, IReadOnlyList<VirtualFile> files, CancellationToken ct)
    {
        var result = new List<ModCredit>();
        // Only path components belonging to this package, never sibling archives.
        var components = root.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var paths = Enumerable.Range(1, components.Length).Select(n => string.Join('/', components.Take(n))).ToList();
        paths.Insert(0, Path.GetFileName(input));
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(path);
            if (Path.GetExtension(name).ToLowerInvariant() is ".zip" or ".rar" or ".7z" or ".rpf") name = Path.GetFileNameWithoutExtension(name);
            var match = ByName.Match(name);
            if (match.Success) Add(result, match.Groups["name"].Value, "Indício no nome", path, Path.GetFileName(path), CreditConfidence.NameHint);
        }
        foreach (var file in files.Where(f => f.Content is not null && !f.IsManaged && !f.IsContainer &&
                     Path.GetExtension(f.Path).ToLowerInvariant() is ".txt" or ".md" or ".markdown").OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            if (!BelongsTo(file.Path, root, roots) || file.Content!.Length > 256 * 1024) continue;
            try
            {
                await using var stream = await file.Content.OpenReadAsync(ct);
                // StreamReader handles UTF BOMs; Latin-1 fallback supports older ANSI readmes.
                using var bytes = new MemoryStream();
                var buffer = new byte[8192];
                int count;
                while ((count = await stream.ReadAsync(buffer, ct)) > 0)
                {
                    if (bytes.Length + count > 256 * 1024) throw new InvalidDataException("Texto muito grande.");
                    bytes.Write(buffer, 0, count);
                }
                bytes.Position = 0;
                string text;
                try { using var reader = new StreamReader(bytes, new UTF8Encoding(false, true), true, leaveOpen: true); text = await reader.ReadToEndAsync(ct); }
                catch (DecoderFallbackException) { text = Encoding.Latin1.GetString(bytes.ToArray()); }
                var lineNumber = 0;
                foreach (var raw in text.Split('\n'))
                {
                    lineNumber++;
                    if (raw.Length > 600) continue;
                    var line = raw.Trim().Trim('\uFEFF').Replace('\t', ' ');
                    if (lineNumber <= 12)
                    {
                        var title = TitleBy.Match(line);
                        if (title.Success) Add(result, title.Groups["name"].Value, "Autoria no título", file.Path, line, CreditConfidence.Explicit);
                    }
                    var match = CreditLine.Match(line.Replace("**", "").Replace("__", ""));
                    if (!match.Success) continue;
                    var role = match.Groups["role"].Value;
                    if (string.IsNullOrWhiteSpace(match.Groups["separator"].Value) &&
                        !role.EndsWith("by", StringComparison.OrdinalIgnoreCase) && !role.EndsWith("por", StringComparison.OrdinalIgnoreCase)) continue;
                    role = role.StartsWith("conv", StringComparison.OrdinalIgnoreCase) ? "Conversão / edição" :
                        role.Contains("work", StringComparison.OrdinalIgnoreCase) ? "Trabalho principal" : "Autor";
                    Add(result, match.Groups["name"].Value, role, file.Path, line, CreditConfidence.Explicit);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
            { /* Optional credits never prevent vehicle import. */ }
        }
        return result.Distinct().Take(128).ToArray();
    }

    private static bool BelongsTo(string path, string root, IReadOnlyCollection<string> roots)
    {
        var a = path.Split('/'); var b = root.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var n = 0;
        while (n < Math.Min(a.Length - 1, b.Length) && a[n].Equals(b[n], StringComparison.OrdinalIgnoreCase)) n++;
        // Never cross into another container (e.g. another car's archive or a FiveM copy).
        if (a.Skip(n).SkipLast(1).Any(p => Path.GetExtension(p).ToLowerInvariant() is ".zip" or ".rar" or ".7z" or ".rpf")) return false;
        if (n == b.Length) return true;
        var common = string.Join('/', b.Take(n));
        return roots.Count(r => common.Length == 0 || r.Equals(common, StringComparison.OrdinalIgnoreCase) || r.StartsWith(common + "/", StringComparison.OrdinalIgnoreCase)) == 1;
    }

    private static void Add(List<ModCredit> result, string value, string role, string path, string evidence, CreditConfidence confidence)
    {
        var name = value.Trim().Trim('*', '`', ':', ' ', '\t');
        if (confidence == CreditConfidence.NameHint) name = VersionSuffix.Replace(name, "").Trim();
        name = Regex.Replace(name, @"\s*\(me\)\s*$", "", RegexOptions.IgnoreCase, Timeout);
        if (name.Length is 0 or > 100 || name.Any(char.IsControl) || name.Contains("http", StringComparison.OrdinalIgnoreCase) ||
            name.IndexOfAny(['/', '\\', '<', '>', '=', ':']) >= 0 || name.Contains(" (", StringComparison.Ordinal) ||
            name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 8 ||
            name.Equals("unknown", StringComparison.OrdinalIgnoreCase) || name.Equals("desconhecido", StringComparison.OrdinalIgnoreCase)) return;
        if (result.Count < 128) result.Add(new(name, role, path, evidence, confidence));
    }

    public static void AppendReport(StringBuilder report, VehiclePackage package)
    {
        if (package.Credits.Count == 0) { report.AppendLine("Autoria: não identificada nos arquivos analisados."); return; }
        report.AppendLine("Autoria encontrada (atribuições conforme as fontes):");
        foreach (var credit in package.Credits)
            report.AppendLine($"- {credit.Name} — {credit.Role} [{credit.Confidence}]").AppendLine($"  Fonte: {credit.SourcePath}").AppendLine($"  Trecho: {credit.Evidence}");
    }
}
