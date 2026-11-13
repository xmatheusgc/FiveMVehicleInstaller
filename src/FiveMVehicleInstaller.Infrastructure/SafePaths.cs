using FiveMVehicleInstaller.Core;
using System.Text.RegularExpressions;

namespace FiveMVehicleInstaller.Infrastructure;

public static partial class SafePaths
{
    public static string NormalizeRelative(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value))
            throw new InvalidDataException($"Caminho inválido: {value}");
        var parts = value.Replace('\\', '/').Split('/');
        foreach (var part in parts)
        {
            if (part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' ') ||
                part.Any(c => c < 32 || "<>:\"|?*".Contains(c)) || ReservedName().IsMatch(part))
                throw new InvalidDataException($"Caminho inseguro: {value}");
        }
        return string.Join('/', parts);
    }

    public static string Within(string root, string relative)
    {
        var basePath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Path.Combine(basePath, NormalizeRelative(relative)));
        if (!target.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("O caminho sai da pasta permitida.");
        return target;
    }

    public static void RejectReparsePoints(string path)
    {
        var current = Path.GetFullPath(path);
        while (current is not null)
        {
            if ((Directory.Exists(current) || File.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Links e junções não são suportados: {current}");
            current = Path.GetDirectoryName(current);
        }
    }

    public static string ValidateResourceName(string name) => ResourceNames.Validate(name);

    public static string SuggestResourceName(string name)
    {
        var result = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9_-]+", "_").Trim('_', '-');
        if (result.Length > 64) result = result[..64];
        if (result.Length == 0 || ReservedName().IsMatch(result)) result = "veiculos";
        return result;
    }

    internal static void DeleteOwnedDirectory(string parent, string directory)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(parent), Path.GetFullPath(directory));
        var verified = Within(parent, relative);
        if (!Directory.Exists(verified)) return;
        RejectReparsePoints(verified);
        // Never recurse through links, even if another process has changed our workspace.
        foreach (var child in Directory.EnumerateFileSystemEntries(verified))
        {
            if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("A pasta temporária contém um link inesperado.");
            if (Directory.Exists(child)) DeleteOwnedDirectory(verified, child);
            else File.Delete(child);
        }
        Directory.Delete(verified);
    }

    [GeneratedRegex("^(con|prn|aux|nul|com[0-9]|lpt[0-9])(?:\\..*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex ReservedName();
}
