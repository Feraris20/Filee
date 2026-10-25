// Validate ISO interoperability and atomic writes for limits, missing inputs, empty images and cancellation.

using DiscUtils.Iso9660;
using Filee.Core.Presets;
using Filee.Engines.Archives;
using static Filee.Engines.Tests.ArchiveBuilders;

namespace Filee.Engines.Tests;

public class IsoWriterTests(EngineFixture fx) : IClassFixture<EngineFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Iso_is_readable_with_unicode_empty_directories_binary_data_and_times()
    {
        var dir = fx.NewFolder();
        var tree = MakeTree(dir, "tree");
        var emptyFile = Path.Combine(tree, "zero.bin");
        await File.WriteAllBytesAsync(emptyFile, [], Ct);
        var iso = Path.Combine(dir, "result.iso");
        await PackAsync(Directory.EnumerateFileSystemEntries(tree).Select(p => new PackItem(p, Path.GetFileName(p))).ToList(), iso);
        using var stream = File.OpenRead(iso);
        using var reader = new CDReader(stream, true);
        foreach (var file in Directory.EnumerateFiles(tree, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetRelativePath(tree, file).Replace('/', '\\');
            using var content = reader.OpenFile(name, FileMode.Open);
            using var bytes = new MemoryStream();
            content.CopyTo(bytes);
            Assert.Equal(await File.ReadAllBytesAsync(file, Ct), bytes.ToArray());
        }
        Assert.True(reader.DirectoryExists("empty"));
        Assert.True(Math.Abs((reader.GetLastWriteTime("readme.txt") - KnownTime).TotalSeconds) <= 2);
    }

    [Fact]
    public async Task Maximum_length_joliet_name_survives()
    {
        var dir = fx.NewFolder();
        var name = new string('한', 60) + ".txt";
        var source = Path.Combine(dir, name);
        await File.WriteAllTextAsync(source, "boundary", Ct);
        var iso = Path.Combine(dir, "result.iso");
        await PackAsync([new PackItem(source, name)], iso);
        using var stream = File.OpenRead(iso);
        using var reader = new CDReader(stream, true);
        Assert.True(reader.FileExists(name));
    }

    [Fact]
    public async Task Empty_input_makes_a_valid_empty_iso()
    {
        var iso = Path.Combine(fx.NewFolder(), "empty.iso");
        await PackAsync([], iso);
        using var stream = File.OpenRead(iso);
        using var reader = new CDReader(stream, true);
        Assert.Empty(reader.GetFileSystemEntries("\\"));
    }

    [Theory]
    [InlineData(65, false)]
    [InlineData(12, true)]
    public async Task Unsupported_names_do_not_replace_existing_outputs(int length, bool semicolon)
    {
        var dir = fx.NewFolder();
        var source = Path.Combine(dir, "source.txt");
        await File.WriteAllTextAsync(source, "input", Ct);
        var name = semicolon ? "file;2.txt" : new string('x', length);
        var iso = Path.Combine(dir, "result.iso");
        await File.WriteAllTextAsync(iso, "existing", Ct);
        await Assert.ThrowsAsync<NotSupportedException>(() => PackAsync([new PackItem(source, name)], iso));
        Assert.Equal("existing", await File.ReadAllTextAsync(iso, Ct));
        Assert.Equal(2, Directory.GetFileSystemEntries(dir).Length);
    }

    [Fact]
    public async Task Missing_input_leaves_no_partial_image()
    {
        var dir = fx.NewFolder();
        await Assert.ThrowsAsync<FileNotFoundException>(() => PackAsync([new PackItem(Path.Combine(dir, "missing"), "missing")], Path.Combine(dir, "result.iso")));
        Assert.Empty(Directory.GetFileSystemEntries(dir));
    }

    [Fact]
    public async Task Cancellation_during_writing_preserves_existing_output_and_removes_temporary_image()
    {
        var dir = fx.NewFolder();
        var source = Path.Combine(dir, "source.bin");
        await File.WriteAllBytesAsync(source, Noise(1 << 20, 3), Ct);
        var iso = Path.Combine(dir, "result.iso");
        await File.WriteAllTextAsync(iso, "existing", Ct);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ArchivePacker(null).PackAsync(
            [new PackItem(source, "source.bin")], iso, "iso", ArchiveLevel.Normal, dir, new CancelProgress(cancel), cancel.Token));
        Assert.Equal("existing", await File.ReadAllTextAsync(iso, Ct));
        Assert.Equal(2, Directory.GetFileSystemEntries(dir).Length);
    }

    private static Task PackAsync(IReadOnlyList<PackItem> items, string output) =>
        new ArchivePacker(null).PackAsync(items, output, "iso", ArchiveLevel.Normal, Path.GetDirectoryName(output)!, null, Ct);

    private sealed class CancelProgress(CancellationTokenSource source) : IProgress<double>
    {
        public void Report(double value) => source.Cancel();
    }
}
