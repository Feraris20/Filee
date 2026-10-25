// Folder-to-ISO end-to-end coverage: no external tools, root layout, Unicode, conflicts, links and custom output.

using DiscUtils.Iso9660;
using Filee.Core.Conversion;
using Filee.Core.Formats;
using Filee.Core.Presets;
using Filee.Engines.Archives;
using static Filee.Engines.Tests.ArchiveBuilders;

namespace Filee.Engines.Tests;

public class FolderIsoTests(EngineFixture fx) : IClassFixture<EngineFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("photos")]
    [InlineData("photos.jpg")]
    [InlineData("backup.tar.gz")]
    [InlineData("Ελληνικά 한글 中文")]
    [InlineData("folder with spaces")]
    public async Task Folder_contents_become_the_disc_root_and_sources_stay_unchanged(string name)
    {
        var dir = fx.NewFolder();
        var source = MakeTree(dir, name);
        await File.WriteAllBytesAsync(Path.Combine(source, "zero.bin"), [], Ct);
        await File.WriteAllTextAsync(Path.Combine(source, "LICENSE"), "no extension", Ct);
        var expected = Snapshot(source);
        var job = await fx.ConvertAsync([source], new Preset { TargetFormat = "iso" });
        Assert.True(job.State == JobState.Completed, string.Join("; ", job.Files.Select(f => f.ErrorDetail)));
        var iso = Assert.Single(job.Outputs);
        Assert.Equal(Path.Combine(dir, name + ".iso"), iso);
        AssertIso(source, iso);
        Assert.Equal(expected, Snapshot(source));
    }

    [Fact]
    public async Task A_folder_iso_round_trips_through_the_independent_7zip_reader()
    {
        Assert.SkipUnless(SevenZipExe is not null, "Bundled 7-Zip required for interoperability check");
        var dir = fx.NewFolder();
        var source = MakeTree(dir, "tree");
        var job = await fx.ConvertAsync([source], new Preset { TargetFormat = "iso" });
        Assert.Equal(JobState.Completed, job.State);
        var extracted = Path.Combine(dir, "extracted");
        await Run7zAsync("x", "-y", "-o" + extracted, Assert.Single(job.Outputs));
        Assert.Equal(Snapshot(source), Snapshot(extracted));
    }

    [Fact]
    public async Task Empty_folder_and_trailing_separator_are_supported_without_7zip()
    {
        var source = Directory.CreateDirectory(Path.Combine(fx.NewFolder(), "empty.folder")).FullName;
        var catalog = new ConverterCatalog([new FolderIsoConverter()]);
        await using var queue = new JobQueue(catalog);
        var job = new ConversionJob([source + Path.DirectorySeparatorChar], new Preset { TargetFormat = "iso" }, "ISO");
        await queue.RunAsync(job);
        Assert.Equal(JobState.Completed, job.State);
        var iso = Assert.Single(job.Outputs);
        Assert.Equal(source + ".iso", iso);
        using var stream = File.OpenRead(iso);
        using var reader = new CDReader(stream, true);
        Assert.Empty(reader.GetFileSystemEntries("\\"));
    }

    [Fact]
    public void Folder_route_does_not_enable_archive_to_iso_indirectly()
    {
        var planner = new RoutePlanner([new ArchiveConverter(), new FolderIsoConverter()]);
        Assert.Single(planner.Plan(FormatRegistry.Folder, "iso")!.Steps);
        Assert.Null(planner.Plan("zip", "iso"));
    }

    [Theory]
    [InlineData(ConflictPolicy.Rename)]
    [InlineData(ConflictPolicy.Skip)]
    [InlineData(ConflictPolicy.Overwrite)]
    public async Task Folder_conversion_honours_existing_iso_conflicts(ConflictPolicy conflict)
    {
        var source = MakeTree(fx.NewFolder(), "tree");
        var destination = source + ".iso";
        await File.WriteAllTextAsync(destination, "existing", Ct);
        var job = await fx.ConvertAsync([source], new Preset { TargetFormat = "iso", Output = { Conflict = conflict } });
        Assert.Equal(JobState.Completed, job.State);
        if (conflict == ConflictPolicy.Skip)
        {
            Assert.Empty(job.Outputs);
            Assert.Equal(FileState.Skipped, job.Files[0].State);
        }
        else
        {
            var output = Assert.Single(job.Outputs);
            Assert.Equal(conflict == ConflictPolicy.Rename ? source + " (2).iso" : destination, output);
            AssertIso(source, output);
        }
        if (conflict != ConflictPolicy.Overwrite)
            Assert.Equal("existing", await File.ReadAllTextAsync(destination, Ct));
    }

    [Fact]
    public async Task Output_inside_a_nested_source_folder_does_not_include_the_previous_image()
    {
        var source = MakeTree(fx.NewFolder(), "tree");
        var outDir = Path.Combine(source, "docs");
        var output = Path.Combine(outDir, "tree.iso");
        await File.WriteAllTextAsync(output, "old image", Ct);
        var job = await fx.ConvertAsync([source], new Preset
        {
            TargetFormat = "iso",
            Output = { Location = OutputLocation.CustomFolder, CustomFolder = outDir, Conflict = ConflictPolicy.Overwrite },
        });
        Assert.Equal(JobState.Completed, job.State);
        Assert.Equal(output, Assert.Single(job.Outputs));
        using var stream = File.OpenRead(output);
        using var reader = new CDReader(stream, true);
        Assert.False(reader.FileExists(@"docs\tree.iso"));
        Assert.True(reader.FileExists("readme.txt"));
        Assert.DoesNotContain(reader.GetFiles("\\", "*.*", SearchOption.AllDirectories), n => n.Contains(".filee-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Several_folders_create_separate_images()
    {
        var dir = fx.NewFolder();
        var first = MakeTree(dir, "first");
        var second = MakeTree(dir, "second");
        var job = await fx.ConvertAsync([first, second], new Preset { TargetFormat = "iso" });
        Assert.Equal(JobState.Completed, job.State);
        Assert.Equal(2, job.Outputs.Count());
        AssertIso(first, first + ".iso");
        AssertIso(second, second + ".iso");
    }

    [Fact]
    public async Task Unsupported_names_fail_without_an_image_and_preserve_sources()
    {
        var source = Directory.CreateDirectory(Path.Combine(fx.NewFolder(), "tree")).FullName;
        var file = Path.Combine(source, new string('x', 65));
        await File.WriteAllTextAsync(file, "keep", Ct);
        var job = await fx.ConvertAsync([source], new Preset { TargetFormat = "iso" });
        Assert.Equal(JobState.Failed, job.State);
        Assert.Empty(job.Outputs);
        Assert.Contains("64 characters", job.Files[0].ErrorDetail);
        Assert.Equal("keep", await File.ReadAllTextAsync(file, Ct));
        Assert.Single(Directory.GetFileSystemEntries(Path.GetDirectoryName(source)!));
    }

    [Fact]
    public async Task Linked_entries_are_skipped_and_a_linked_source_folder_is_rejected()
    {
        var dir = fx.NewFolder();
        var source = MakeTree(dir, "tree");
        var outside = Directory.CreateDirectory(Path.Combine(dir, "outside")).FullName;
        await File.WriteAllTextAsync(Path.Combine(outside, "secret.txt"), "private", Ct);
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(source, "link"), outside);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException
                                   || ex is IOException && (ex.HResult & 0xffff) == 1314)
        {
            Assert.Skip("Creating symbolic links is not permitted on this machine.");
        }
        Directory.CreateSymbolicLink(Path.Combine(source, "docs", "cycle"), source);
        File.CreateSymbolicLink(Path.Combine(source, "secret.txt"), Path.Combine(outside, "secret.txt"));
        var job = await fx.ConvertAsync([source], new Preset { TargetFormat = "iso" });
        Assert.Equal(JobState.Completed, job.State);
        using (var stream = File.OpenRead(Assert.Single(job.Outputs)))
        using (var reader = new CDReader(stream, true))
        {
            Assert.False(reader.DirectoryExists("link"));
            Assert.False(reader.DirectoryExists(@"docs\cycle"));
            Assert.False(reader.FileExists("secret.txt"));
        }
        var linked = Path.Combine(dir, "linked");
        Directory.CreateSymbolicLink(linked, source);
        var rejected = await fx.ConvertAsync([linked], new Preset { TargetFormat = "iso" });
        Assert.Equal(JobState.Failed, rejected.State);
        Assert.Contains("linked folder", rejected.Files[0].ErrorDetail);
        Assert.Empty(rejected.Outputs);
    }

    private static void AssertIso(string source, string iso)
    {
        using var stream = File.OpenRead(iso);
        using var reader = new CDReader(stream, true);
        var files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
        Assert.Equal(files.Length, reader.GetFiles("\\", "*.*", SearchOption.AllDirectories).Length);
        foreach (var file in files)
        {
            var name = Path.GetRelativePath(source, file).Replace('/', '\\');
            using var content = reader.OpenFile(name, FileMode.Open);
            using var bytes = new MemoryStream();
            content.CopyTo(bytes);
            Assert.Equal(File.ReadAllBytes(file), bytes.ToArray());
        }
        foreach (var folder in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Assert.True(reader.DirectoryExists(Path.GetRelativePath(source, folder).Replace('/', '\\')));
        Assert.True(Math.Abs((reader.GetLastWriteTime("readme.txt") - KnownTime).TotalSeconds) <= 2);
    }
}
