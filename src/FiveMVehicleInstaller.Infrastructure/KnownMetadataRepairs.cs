using FiveMVehicleInstaller.Core;

namespace FiveMVehicleInstaller.Infrastructure;

// Offers a single, validated byte edit. Approval belongs to the caller; analysis never applies it.
internal static class KnownMetadataRepairs
{
    public static async Task<IContentSource?> TryCorrectVisibleModsAsync(IContentSource source, CancellationToken cancellationToken)
    {
        if (source.Length > 16 * 1024 * 1024) return null;
        await using var stream = await source.OpenReadAsync(cancellationToken);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        var original = buffer.ToArray();
        var opening = "<visibleMods>"u8.ToArray();
        var closing = "</linkMods>"u8.ToArray();
        var position = -1;
        for (var i = 0; i <= original.Length - opening.Length; i++)
        {
            if (!original.AsSpan(i, opening.Length).SequenceEqual(opening)) continue;
            var end = i + opening.Length;
            while (end < original.Length && original[end] is 9 or 10 or 13 or 32) end++;
            if (original.Length - end < closing.Length || !original.AsSpan(end, closing.Length).SequenceEqual(closing)) continue;
            if (position >= 0) return null; // Ambiguous input is never repaired.
            position = end;
        }
        if (position < 0) return null;
        var replacement = "</visibleMods>"u8.ToArray();
        var corrected = new byte[original.Length + replacement.Length - closing.Length];
        original.AsSpan(0, position).CopyTo(corrected);
        replacement.CopyTo(corrected, position);
        original.AsSpan(position + closing.Length).CopyTo(corrected.AsSpan(position + replacement.Length));
        var content = new MemoryContentSource(corrected);
        try
        {
            var xml = await Metadata.ReadAsync(content, cancellationToken);
            return xml.Root?.Name.LocalName == "CVehicleModelInfoVarGlobal" ? content : null;
        }
        catch (System.Xml.XmlException) { return null; }
    }
}