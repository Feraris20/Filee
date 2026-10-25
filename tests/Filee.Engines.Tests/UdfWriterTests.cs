// UDF-specific names, empty images, cancellation, overwrite safety and semicolon/equals graft escaping.

using Filee.Core.Presets;
using Filee.Engines.Archives;
using static Filee.Engines.Tests.ArchiveBuilders;

namespace Filee.Engines.Tests;

public class UdfWriterTests(EngineFixture fx) : IClassFixture<EngineFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static void Require() => Assert.SkipUnless(MkisofsWriter.Locate() is not null && SevenZipExe is not null, "Bundled mkisofs and 7-Zip required");

    [Fact]
    public async Task Long_unicode_names_and_equals_and_semicolons_survive_udf()
    {
        Require();
        var dir = fx.NewFolder();
        var tree = MakeTree(dir, "source = tree");
        await File.WriteAllTextAsync(Path.Combine(tree, new string('한', 80) + ".txt"), "long Unicode", Ct);
        await File.WriteAllTextAsync(Path.Combine(tree, new string('x', 200) + ".txt"), "long ASCII", Ct);
        await File.WriteAllTextAsync(Path.Combine(tree, "a=b;2.txt"), "escaped", Ct);
        var iso = Path.Combine(dir, "result.iso");
        await PackAsync([new PackItem(tree, "source = tree")], iso);
        var extracted = Path.Combine(dir, "check");
        await Run7zAsync("x", "-tudf", "-y", "-o" + extracted, iso);
        Assert.Equal(Snapshot(tree), Snapshot(Path.Combine(extracted, "source = tree")));
    }

    [Fact]
    public async Task Completely_empty_udf_input_is_rejected_without_an_unreadable_image()
    {
        Require();
        var dir = fx.NewFolder();
        var iso = Path.Combine(dir, "empty.iso");
        var error = await Assert.ThrowsAsync<NotSupportedException>(() => PackAsync([], iso));
        Assert.Contains("completely empty", error.Message);
        Assert.Empty(Directory.GetFileSystemEntries(dir));
    }

    [Theory]
    [InlineData("too-long")]
    [InlineData("newline")]
    public async Task Unsupported_udf_names_preserve_the_existing_image(string kind)
    {
        Require();
        var dir = fx.NewFolder();
        var source = Path.Combine(dir, "input.txt");
        await File.WriteAllTextAsync(source, "input", Ct);
        var iso = Path.Combine(dir, "existing.iso");
        await File.WriteAllTextAsync(iso, "keep", Ct);
        var name = kind == "too-long" ? new string('한', 128) : "line\nbreak.txt";
        await Assert.ThrowsAsync<NotSupportedException>(() => PackAsync([new PackItem(source, name)], iso));
        Assert.Equal("keep", await File.ReadAllTextAsync(iso, Ct));
        Assert.Equal(2, Directory.GetFileSystemEntries(dir).Length);
    }

    [Fact]
    public async Task Cancellation_before_native_writing_cleans_up_and_preserves_the_image()
    {
        Require();
        var dir = fx.NewFolder();
        var source = Path.Combine(dir, "input.txt");
        await File.WriteAllTextAsync(source, "input", Ct);
        var iso = Path.Combine(dir, "existing.iso");
        await File.WriteAllTextAsync(iso, "keep", Ct);
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ArchivePacker(null).PackAsync(
            [new PackItem(source, "input.txt")], iso, "iso", ArchiveLevel.Normal, dir, null, cancelled.Token, IsoStandard.Udf102));
        Assert.Equal("keep", await File.ReadAllTextAsync(iso, Ct));
        Assert.Equal(2, Directory.GetFileSystemEntries(dir).Length);
    }

    private static Task PackAsync(IReadOnlyList<PackItem> items, string iso) => new ArchivePacker(null).PackAsync(
        items, iso, "iso", ArchiveLevel.Normal, Path.GetDirectoryName(iso)!, null, Ct, IsoStandard.Udf102);
}
