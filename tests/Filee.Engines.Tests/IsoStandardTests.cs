// Validate each ISO writer mode independently of conversion routes and of the filesystem reader's display choice.

using System.Text;
using Filee.Core.Presets;
using Filee.Engines.Archives;
using static Filee.Engines.Tests.ArchiveBuilders;

namespace Filee.Engines.Tests;

public class IsoStandardTests(EngineFixture fx) : IClassFixture<EngineFixture>
{
    [Theory]
    [InlineData(IsoStandard.Iso9660)]
    [InlineData(IsoStandard.Iso9660Joliet)]
    [InlineData(IsoStandard.Udf102)]
    public async Task The_writer_creates_the_selected_filesystem(IsoStandard standard)
    {
        Assert.SkipUnless(SevenZipExe is not null, "Bundled 7-Zip required");
        if (standard == IsoStandard.Udf102)
            Assert.SkipUnless(Filee.Engines.Archives.MkisofsWriter.Locate() is not null, "Bundled mkisofs required");
        var dir = fx.NewFolder();
        var tree = MakeTree(dir, "tree");
        var iso = Path.Combine(dir, "image.iso");
        var items = Directory.EnumerateFileSystemEntries(tree).Select(path => new PackItem(path, Path.GetFileName(path))).ToList();
        await new ArchivePacker(null).PackAsync(items, iso, "iso", ArchiveLevel.Normal, dir, null, TestContext.Current.CancellationToken, standard);
        AssertFilesystem(iso, standard);
        // Validate the filesystem descriptor itself, independently of a reader's variant-reporting API.
        var bytes = await File.ReadAllBytesAsync(iso, TestContext.Current.CancellationToken);
        var extracted = Path.Combine(dir, "extracted");
        if (standard == IsoStandard.Udf102)
        {
            Assert.Contains("NSR02", Encoding.ASCII.GetString(bytes, 16 * 2048, 16 * 2048));
            await Run7zAsync("x", "-tudf", "-y", "-o" + extracted, iso);
        }
        else
            await Run7zAsync("x", "-y", "-o" + extracted, iso);
        var expected = Snapshot(tree);
        var actual = Snapshot(extracted);
        Assert.Equal(expected.Values.Order(), actual.Values.Order());
        if (standard != IsoStandard.Iso9660)
            Assert.Equal(expected, actual);
        else
            Assert.All(actual.Keys, name =>
            {
                Assert.Equal(name.ToUpperInvariant(), name);
                Assert.All(name, character => Assert.True(character <= 127));
            });
    }

    internal static void AssertFilesystem(string iso, IsoStandard standard)
    {
        var bytes = File.ReadAllBytes(iso);
        Assert.Equal("CD001", Encoding.ASCII.GetString(bytes, 17 * 2048 + 1, 5));
        Assert.Equal(standard == IsoStandard.Iso9660Joliet ? "%/E" : "\0\0\0", Encoding.ASCII.GetString(bytes, 17 * 2048 + 88, 3));
        if (standard == IsoStandard.Udf102)
            Assert.Contains("NSR02", Encoding.ASCII.GetString(bytes, 16 * 2048, 16 * 2048));
    }
}
