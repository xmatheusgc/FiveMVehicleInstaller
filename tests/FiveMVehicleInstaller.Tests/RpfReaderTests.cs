using System.Buffers.Binary;
using System.Text;
using FiveMVehicleInstaller.Infrastructure;

namespace FiveMVehicleInstaller.Tests;

public sealed class RpfReaderTests
{
    [Fact]
    public async Task ReadsSubdirectoriesAndDecompressesBinaryFiles()
    {
        using var workspace = new TestWorkspace();
        var expected = Encoding.UTF8.GetBytes(new string('x', 10000));
        var path = workspace.Write("test.rpf", RpfFixture.Create(new("data/test.meta", expected, true), new("readme.txt", [1, 2, 3])));
        var files = new Rpf7Reader().Read(path);
        Assert.Equal(2, files.Count);
        await using var stream = await files.Single(f => f.Path == "data/test.meta").Content.OpenReadAsync();
        using var output = new MemoryStream(); await stream.CopyToAsync(output);
        Assert.Equal(expected, output.ToArray());
    }

    [Theory]
    [InlineData(32)]
    [InlineData(0x1000000)]
    public async Task ReconstructsResourceHeaderIncludingLargeResourceSize(int payloadSize)
    {
        using var workspace = new TestWorkspace();
        var payload = new byte[payloadSize]; payload[^1] = 123;
        var expected = RpfFixture.Resource(payload);
        var path = workspace.Write("test.rpf", RpfFixture.Create(new FixtureFile("car.yft", expected, Resource: true)));
        var file = Assert.Single(new Rpf7Reader().Read(path));
        await using var stream = await file.Content.OpenReadAsync();
        using var output = new MemoryStream(); await stream.CopyToAsync(output);
        Assert.Equal(expected, output.ToArray());
    }

    [Fact]
    public void RejectsEncryptedArchives()
    {
        using var workspace = new TestWorkspace(); var bytes = RpfFixture.Create(new FixtureFile("x", [1]));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 0x0FFFFFF9);
        Assert.Throws<NotSupportedException>(() => new Rpf7Reader().Read(workspace.Write("encrypted.rpf", bytes)));
    }

    [Fact]
    public async Task AcceptsEmptyBinaryEntryWithZeroOffset()
    {
        using var workspace = new TestWorkspace(); var bytes = RpfFixture.Create(new FixtureFile("empty.txt", []));
        bytes[37] = bytes[38] = bytes[39] = 0;
        var file = Assert.Single(new Rpf7Reader().Read(workspace.Write("empty.rpf", bytes)));
        await using var stream = await file.Content.OpenReadAsync();
        Assert.Equal(0, stream.ReadByte() + 1);
    }

    [Fact]
    public void RejectsTruncatedIndexAndDirectoryCycles()
    {
        using var workspace = new TestWorkspace();
        Assert.Throws<InvalidDataException>(() => new Rpf7Reader().Read(workspace.Write("truncated.rpf", RpfFixture.Create(new FixtureFile("x", [1]))[..20])));
        var cyclic = RpfFixture.Create(new FixtureFile("x", [1])); BinaryPrimitives.WriteUInt32LittleEndian(cyclic.AsSpan(24), 0);
        Assert.Throws<InvalidDataException>(() => new Rpf7Reader().Read(workspace.Write("cycle.rpf", cyclic)));
    }

    [Fact]
    public async Task RejectsIncorrectDecompressedSize()
    {
        using var workspace = new TestWorkspace(); var bytes = RpfFixture.Create(new FixtureFile("test.meta", Encoding.UTF8.GetBytes(new string('x', 100)), true));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 2);
        var entry = Assert.Single(new Rpf7Reader().Read(workspace.Write("wrong-size.rpf", bytes)));
        await using var stream = await entry.Content.OpenReadAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => stream.CopyToAsync(Stream.Null));
    }

    [Theory]
    [InlineData("../outside")][InlineData("C:/outside")][InlineData("x/../../outside")]
    [InlineData("x/NUL.txt")][InlineData("x/file.")][InlineData("x:stream")]
    public void RejectsUnsafePaths(string path) => Assert.Throws<InvalidDataException>(() => SafePaths.NormalizeRelative(path));
}
