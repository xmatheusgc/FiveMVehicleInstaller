// RPF7 entry layout adapted from RageLib, Copyright(c) 2015 Neodymium (MIT).
// Revision e8c7cfccc735aeb2ef8b61dd3ea9559db6990f43. See THIRD-PARTY-NOTICES.txt.
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using FiveMVehicleInstaller.Core;

namespace FiveMVehicleInstaller.Infrastructure;

public sealed class Rpf7Reader : IRpfReader
{
    private sealed record Entry(string Name, bool Directory, bool Resource, bool Encrypted, bool Compressed,
        uint Index, uint Count, long Offset, long Size, uint Uncompressed, uint SystemFlags, uint GraphicsFlags);

    public IReadOnlyList<RpfFileEntry> Read(string archivePath)
    {
        SafePaths.RejectReparsePoints(archivePath);
        var source = new FileContentSource(archivePath);
        using var stream = File.OpenRead(archivePath);
        using var reader = new BinaryReader(stream, Encoding.UTF8, true);
        if (stream.Length < 16 || reader.ReadUInt32() != 0x52504637)
            throw new InvalidDataException("Formato não suportado. Esperado RPF7 de GTA V Legacy.");
        var count = reader.ReadUInt32();
        var namesLength = reader.ReadUInt32();
        var encryption = reader.ReadUInt32();
        if (encryption is not (0x4E45504F or 0x0FFFFFFF))
            throw new NotSupportedException("RPF criptografado (AES/NG) não é suportado nesta versão.");
        var tableEnd = 16L + count * 16L + namesLength;
        if (count is 0 or > 1_000_000 || namesLength > 64 * 1024 * 1024 || tableEnd > stream.Length)
            throw new InvalidDataException("Índice RPF inválido ou truncado.");
        var table = reader.ReadBytes(checked((int)count * 16));
        var names = reader.ReadBytes((int)namesLength);
        var entries = new Entry[count];
        for (var i = 0; i < entries.Length; i++)
        {
            var data = table.AsSpan(i * 16, 16);
            var second = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
            var directory = second == 0x7FFFFF00;
            var resource = !directory && (second & 0x80000000) != 0;
            var nameOffset = directory ? BinaryPrimitives.ReadUInt32LittleEndian(data) : BinaryPrimitives.ReadUInt16LittleEndian(data);
            if (nameOffset >= names.Length) throw new InvalidDataException("Nome fora do índice RPF.");
            var nameEnd = Array.IndexOf(names, (byte)0, (int)nameOffset);
            if (nameEnd < 0) throw new InvalidDataException("Nome RPF sem terminador.");
            var name = Encoding.UTF8.GetString(names, (int)nameOffset, nameEnd - (int)nameOffset);
            if (i != 0) { SafePaths.NormalizeRelative(name); if (name.Contains('/') || name.Contains('\\')) throw new InvalidDataException("Nome RPF inválido."); }
            var a = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
            var b = BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);
            if (directory)
            {
                if ((long)a + b > count) throw new InvalidDataException("Diretório fora do índice RPF.");
                entries[i] = new(name, true, false, false, false, a, b, 0, 0, 0, 0, 0);
                continue;
            }
            var packedSize = Read24(data[2..]);
            var offset = (long)(Read24(data[5..]) & (resource ? 0x7FFFFFu : 0xFFFFFFu)) * 512;
            long size = packedSize;
            if (resource && size == 0xFFFFFF)
            {
                if (offset + 16 > stream.Length) throw new InvalidDataException("Cabeçalho de recurso truncado.");
                stream.Position = offset;
                var header = reader.ReadBytes(16);
                size = (uint)header[7] | ((uint)header[14] << 8) | ((uint)header[5] << 16) | ((uint)header[2] << 24);
            }
            if (!resource && packedSize == 0) size = a;
            if ((size > 0 && offset < tableEnd) || offset + size > stream.Length || (resource && size < 16))
                throw new InvalidDataException($"Dados fora dos limites do RPF: {name} (entrada {i}, offset {offset}, tamanho {size}, arquivo {stream.Length}).");
            if (!resource && b > 1) throw new InvalidDataException("Indicador de criptografia de arquivo inválido.");
            entries[i] = new(name, false, resource, !resource && b == 1, !resource && packedSize != 0, 0, 0, offset, size,
                resource ? 0 : a, resource ? a : 0, resource ? b : 0);
        }
        if (!entries[0].Directory) throw new InvalidDataException("Raiz RPF não é um diretório.");
        var visited = new HashSet<int>();
        var result = new List<RpfFileEntry>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(int index, string parent, int depth)
        {
            if (depth > 64 || !visited.Add(index)) throw new InvalidDataException("Ciclo ou profundidade inválida no RPF.");
            var entry = entries[index];
            var path = index == 0 ? "" : (parent.Length == 0 ? entry.Name : parent + "/" + entry.Name);
            if (index != 0 && !paths.Add(path)) throw new InvalidDataException("Caminhos duplicados no RPF.");
            if (entry.Directory)
            {
                for (var child = entry.Index; child < (long)entry.Index + entry.Count; child++)
                    Visit((int)child, path, depth + 1);
            }
            else
            {
                if (entry.Encrypted) throw new NotSupportedException($"Arquivo criptografado dentro do RPF: {path}");
                result.Add(new(path, new RpfContentSource(source, entry)));
            }
        }
        Visit(0, "", 0);
        if (visited.Count != entries.Length) throw new InvalidDataException("Índice RPF contém entradas sem diretório.");
        return result;
    }

    private static uint Read24(ReadOnlySpan<byte> bytes) => (uint)(bytes[0] | bytes[1] << 8 | bytes[2] << 16);

    private sealed class RpfContentSource(FileContentSource archive, Entry entry) : IContentSource
    {
        public long Length => entry.Resource ? entry.Size : entry.Uncompressed;
        public async ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
        {
            var file = await archive.OpenReadAsync(cancellationToken);
            try
            {
                if (entry.Resource)
                {
                    var header = new byte[16];
                    BinaryPrimitives.WriteUInt32LittleEndian(header, 0x37435352); // RSC7
                    var version = ((entry.SystemFlags >> 28) << 4) | (entry.GraphicsFlags >> 28);
                    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), version);
                    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), entry.SystemFlags);
                    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), entry.GraphicsFlags);
                    return new PrefixStream(header, new SliceStream(file, entry.Offset + 16, entry.Size - 16));
                }
                var slice = new SliceStream(file, entry.Offset, entry.Size);
                return !entry.Compressed
                    ? new ExactLengthStream(slice, entry.Uncompressed)
                    : new ExactLengthStream(new DeflateStream(slice, CompressionMode.Decompress), entry.Uncompressed);
            }
            catch { file.Dispose(); throw; }
        }
    }
}
