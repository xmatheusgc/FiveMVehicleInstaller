using System.Text;
using System.Xml;
using System.Xml.Linq;
using FiveMVehicleInstaller.Core;

namespace FiveMVehicleInstaller.Infrastructure;

internal static class Metadata
{
    public static readonly IReadOnlyDictionary<string, string> Types = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["CVehicleModelInfo__InitDataList"] = "VEHICLE_METADATA_FILE",
        ["CHandlingDataMgr"] = "HANDLING_FILE",
        ["CVehicleModelInfoVarGlobal"] = "CARCOLS_FILE",
        ["CVehicleModelInfoVariation"] = "VEHICLE_VARIATION_FILE",
        ["CVehicleMetadataMgr"] = "VEHICLE_LAYOUTS_FILE"
    };

    public static async Task<XDocument> ReadAsync(IContentSource source, CancellationToken cancellationToken)
    {
        if (source.Length > 16 * 1024 * 1024) throw new InvalidDataException("XML excede o limite de 16 MiB.");
        await using var stream = await source.OpenReadAsync(cancellationToken);
        return await ReadDocumentAsync(stream, LoadOptions.PreserveWhitespace, cancellationToken);
    }

    internal static async Task<XDocument> ReadDocumentAsync(Stream stream, LoadOptions options, CancellationToken cancellationToken)
    {
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            Async = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = 16 * 1024 * 1024
        });
        return await XDocument.LoadAsync(reader, options, cancellationToken);
    }

    public static string Value(XElement element, string name) => element.Elements()
        .FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim() ?? "";

    public static XElement[] VehicleItems(XDocument document) => document.Root?.Elements()
        .FirstOrDefault(e => e.Name.LocalName == "InitDatas")?.Elements()
        .Where(e => e.Name.LocalName == "Item").ToArray() ?? [];

    public static async Task WriteAudioAsync(PackageFile file, VehiclePackage package,
        IReadOnlyDictionary<string, string> overrides, Stream destination, CancellationToken cancellationToken)
    {
        var changes = package.Vehicles.Where(v => v.MetadataPath == file.SourcePath &&
            overrides.TryGetValue(v.Key, out var value) && value.Trim() != v.AudioNameHash).ToArray();
        await using var original = await file.Content.OpenReadAsync(cancellationToken);
        if (changes.Length == 0)
        {
            await original.CopyToAsync(destination, cancellationToken);
            return;
        }
        if (file.Content.Length > 16 * 1024 * 1024) throw new InvalidDataException("XML excede o limite de 16 MiB.");
        using var buffer = new MemoryStream();
        await original.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        var document = await ReadDocumentAsync(buffer, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo, cancellationToken);
        var items = VehicleItems(document);
        var patcher = new XmlBytePatcher(buffer.ToArray(), document.Declaration?.Encoding);
        foreach (var vehicle in changes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = overrides[vehicle.Key].Trim();
            if (value.Length > 256 || value.Any(char.IsControl))
                throw new InvalidDataException("O identificador de som contém caracteres inválidos ou excede 256 caracteres.");
            XmlConvert.VerifyXmlChars(value);
            if (vehicle.ItemIndex >= items.Length || Value(items[vehicle.ItemIndex], "modelName") != vehicle.ModelName)
                throw new InvalidDataException("Os metadados mudaram. Importe o pacote novamente.");
            var item = items[vehicle.ItemIndex];
            var audioElements = item.Elements().Where(e => e.Name.LocalName == "audioNameHash").ToArray();
            if (audioElements.Length > 1) throw new InvalidDataException("O veículo possui mais de um audioNameHash.");
            if (audioElements.Length == 0) patcher.InsertChild(item, "audioNameHash", value);
            else patcher.ReplaceValue(audioElements[0], value);
        }
        // Copy every untouched byte from the input; never serialize metadata back to XML.
        await patcher.WriteAsync(destination, cancellationToken);
    }
}
