using System.Text.RegularExpressions;

namespace FiveMVehicleInstaller.Core;

/// <summary>Portable resource folder names. This policy never changes GTA model or asset identifiers.</summary>
public static partial class ResourceNames
{
    public static string NormalizeInput(string? name) => Spaces().Replace((name ?? "").Trim().ToLowerInvariant(), "_");

    public static string ValidationError(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "Informe um nome para o resource.";
        if (Reserved().IsMatch(name)) return "Esse nome é reservado pelo Windows. Escolha outro.";
        if (!Allowed().IsMatch(name)) return "Comece com letra ou número e use até 64 letras minúsculas, números, _ ou -.";
        return "";
    }

    public static string Validate(string name)
    {
        var error = ValidationError(name);
        if (error.Length > 0) throw new InvalidDataException(error);
        return name;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
    [GeneratedRegex(@"\A[a-z0-9][a-z0-9_-]{0,63}\z")]
    private static partial Regex Allowed();
    [GeneratedRegex(@"\A(con|prn|aux|nul|com[0-9]|lpt[0-9])\z", RegexOptions.IgnoreCase)]
    private static partial Regex Reserved();
}