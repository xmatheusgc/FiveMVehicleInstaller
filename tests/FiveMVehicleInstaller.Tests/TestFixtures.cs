using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using FiveMVehicleInstaller.Infrastructure;

namespace FiveMVehicleInstaller.Tests;

internal sealed class TestWorkspace : IDisposable
{
    private readonly string parent = Path.GetFullPath(Environment.GetEnvironmentVariable("FIVEM_INSTALLER_TEST_ROOT") ??
        Path.Combine(Path.GetTempPath(), "FiveMVehicleInstallerTests"));
    public string Root { get; }
    public TestWorkspace() { Root = SafePaths.Within(parent, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Root); }
    public string PathFor(string path) => SafePaths.Within(Root, path);
    public string Write(string path, byte[] data)
    {
        var target = PathFor(path); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllBytes(target, data); return target;
    }
    public string WriteText(string path, string data) => Write(path, Encoding.UTF8.GetBytes(data));
    public void Dispose()
    {
        var verified = SafePaths.Within(parent, Path.GetRelativePath(parent, Root));
        SafePaths.RejectReparsePoints(verified);
        if (Directory.Exists(verified)) Directory.Delete(verified, true);
    }
}

internal sealed record FixtureFile(string Path, byte[] Data, bool Compressed = false, bool Resource = false);

internal static class RpfFixture
{
    private sealed class Node(string name)
    {
        public string Name { get; } = name;
        public Dictionary<string, Node> Children { get; } = new(StringComparer.Ordinal);
        public FixtureFile? File;
        public int NameOffset, Index, Count;
        public long Offset;
        public byte[] Data = [];
    }
    public static byte[] Resource(byte[] payload, uint systemFlags = 0xA0000000, uint graphicsFlags = 0x20000000)
    {
        var result = new byte[16 + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(result, 0x37435352);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), ((systemFlags >> 28) << 4) | (graphicsFlags >> 28));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), systemFlags);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), graphicsFlags);
        payload.CopyTo(result, 16); return result;
    }
    public static byte[] Create(params FixtureFile[] files)
    {
        var root = new Node("");
        foreach (var file in files)
        {
            var current = root;
            foreach (var part in file.Path.Split('/'))
            {
                if (!current.Children.TryGetValue(part, out var next)) current.Children.Add(part, next = new Node(part));
                current = next;
            }
            current.File = file;
        }
        var nodes = new List<Node> { root };
        for (var i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].File is not null) continue;
            nodes[i].Index = nodes.Count; nodes[i].Count = nodes[i].Children.Count;
            nodes.AddRange(nodes[i].Children.Values);
        }
        using var names = new MemoryStream();
        foreach (var node in nodes)
        {
            node.NameOffset = (int)names.Length; names.Write(Encoding.UTF8.GetBytes(node.Name)); names.WriteByte(0);
        }
        var nextOffset = Align(16 + nodes.Count * 16 + names.Length);
        foreach (var node in nodes.Where(n => n.File is not null))
        {
            var file = node.File!; node.Data = file.Data.ToArray();
            if (file.Compressed)
            {
                using var compressed = new MemoryStream();
                using (var deflate = new DeflateStream(compressed, CompressionLevel.SmallestSize, true)) deflate.Write(file.Data);
                node.Data = compressed.ToArray();
            }
            if (file.Resource && node.Data.Length >= 0xFFFFFF)
            {
                var size = (uint)node.Data.Length;
                node.Data[7] = (byte)size; node.Data[14] = (byte)(size >> 8);
                node.Data[5] = (byte)(size >> 16); node.Data[2] = (byte)(size >> 24);
            }
            node.Offset = nextOffset; nextOffset = Align(nextOffset + node.Data.Length);
        }
        using var output = new MemoryStream(new byte[checked((int)nextOffset)], true);
        using var writer = new BinaryWriter(output, Encoding.UTF8, true);
        writer.Write(0x52504637u); writer.Write((uint)nodes.Count); writer.Write((uint)names.Length); writer.Write(0x4E45504Fu);
        foreach (var node in nodes)
        {
            if (node.File is null)
            { writer.Write((uint)node.NameOffset); writer.Write(0x7FFFFF00u); writer.Write((uint)node.Index); writer.Write((uint)node.Count); }
            else
            {
                var file = node.File; writer.Write((ushort)node.NameOffset);
                Write24(writer, file.Resource ? (uint)Math.Min(node.Data.Length, 0xFFFFFF) : file.Compressed ? (uint)node.Data.Length : 0);
                Write24(writer, (uint)(node.Offset / 512) | (file.Resource ? 0x800000u : 0));
                writer.Write(file.Resource ? BinaryPrimitives.ReadUInt32LittleEndian(file.Data.AsSpan(8)) : (uint)file.Data.Length);
                writer.Write(file.Resource ? BinaryPrimitives.ReadUInt32LittleEndian(file.Data.AsSpan(12)) : 0u);
            }
        }
        writer.Write(names.ToArray());
        foreach (var node in nodes.Where(n => n.File is not null)) { output.Position = node.Offset; output.Write(node.Data); }
        return output.ToArray();
    }
    private static long Align(long length) => (length + 511) / 512 * 512;
    private static void Write24(BinaryWriter writer, uint value) { writer.Write((byte)value); writer.Write((byte)(value >> 8)); writer.Write((byte)(value >> 16)); }
}

internal static class PackageFixture
{
    public static string Vehicles(params (string Model, string Sound)[] vehicles) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n<CVehicleModelInfo__InitDataList><InitDatas>" +
        string.Join("", vehicles.Select(v => $"<Item><modelName>{v.Model}</modelName><txdName>{v.Model}</txdName><handlingId>TEST</handlingId><audioNameHash>{v.Sound}</audioNameHash><unchanged value=\"42\" /></Item>")) +
        "</InitDatas></CVehicleModelInfo__InitDataList>";
    public static FixtureFile[] Files(string model = "car") =>
    [
        new("setup2.xml", Encoding.UTF8.GetBytes($"<SSetupData><nameHash>{model}</nameHash></SSetupData>")),
        new("content.xml", Encoding.UTF8.GetBytes("<CDataFileMgr__ContentsOfDataFileXml/>")),
        new("data/vehicles.meta", Encoding.UTF8.GetBytes(Vehicles((model, "SULTAN"))), true),
        new("data/handling.meta", Encoding.UTF8.GetBytes("<CHandlingDataMgr><HandlingData/></CHandlingDataMgr>"), true),
        new("x64/vehicles.rpf", RpfFixture.Create(new(model + ".yft", RpfFixture.Resource([1, 2, 3, 4]), Resource: true),
            new(model + ".ytd", RpfFixture.Resource([5, 6]), Resource: true)))
    ];
    public static string WriteFolder(TestWorkspace workspace, string folder, string model = "car")
    {
        foreach (var file in Files(model)) workspace.Write(folder + "/" + file.Path, file.Data);
        return workspace.PathFor(folder);
    }
}
