// Exercise every readable archive through ISO, with independent extraction, password failures and cleanup.

using System.IO.Compression;
using System.Text;
using DiscUtils.Iso9660;
using Filee.Core.Conversion;
using Filee.Core.Formats;
using Filee.Core.Presets;
using Filee.Engines.Archives;
using static Filee.Engines.Tests.ArchiveBuilders;

namespace Filee.Engines.Tests;

public class ArchiveIsoTests(EngineFixture fx) : IClassFixture<EngineFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    public static IEnumerable<object[]> Formats => ArchiveExtractor.ReadableFormats.Select(f => new object[] { f }).Append(["wbpz"]).Append(["rar5"]);

    [Theory]
    [MemberData(nameof(Formats))]
    public void Every_supported_archive_has_a_direct_iso_route(string format)
    {
        var route = fx.Catalog.CreatePlanner(["archive"]).Plan(SourceFormat(format), "iso");
        Assert.Equal("archive", Assert.Single(Assert.IsType<Route>(route).Steps).Converter.Id);
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task Every_supported_archive_round_trips_through_iso(string format)
    {
        Assert.SkipUnless(SevenZipExe is not null, "Bundled 7-Zip required");
        var dir = fx.NewFolder();
        var source = await CreateSourceAsync(dir, format);
        var original = await File.ReadAllBytesAsync(source, Ct);
        var scratch = Directory.CreateDirectory(Path.Combine(dir, "expected")).FullName;
        var unpacked = await new ArchiveExtractor(SevenZip.Locate()).ExtractAsync(source, SourceFormat(format), scratch, Ct);
        var expected = Snapshot(unpacked);
        Assert.NotEmpty(expected);
        var job = await fx.ConvertAsync([source], new Preset { TargetFormat = "iso" });
        Assert.True(job.State == JobState.Completed, string.Join("; ", job.Files.Select(f => f.ErrorDetail)));
        var iso = Assert.Single(job.Outputs);
        Assert.EndsWith(".iso", iso);
        var actual = Path.Combine(dir, "actual");
        await Run7zAsync("x", "-y", "-o" + actual, iso);
        Assert.Equal(expected, Snapshot(actual));
        Assert.Equal(original, await File.ReadAllBytesAsync(source, Ct));
    }

    [Theory]
    [InlineData("zip", "-mem=ZipCrypto")]
    [InlineData("zip", "-mem=AES256")]
    [InlineData("7z", "-mhe=off")]
    [InlineData("7z", "-mhe=on")]
    public async Task Password_protected_zip_and_7z_fail_without_an_iso(string format, string encryption)
    {
        Assert.SkipUnless(SevenZipExe is not null, "Bundled 7-Zip required");
        var dir = fx.NewFolder();
        var secret = Path.Combine(dir, "secret.txt");
        await File.WriteAllTextAsync(secret, "secret", Ct);
        var archive = Path.Combine(dir, "locked." + format);
        await Run7zAsync("a", "-t" + format, "-ppassword", encryption, archive, secret);
        await AssertLockedAsync(archive);
        Assert.Equal(["locked." + format, "secret.txt"], Directory.GetFileSystemEntries(dir).Select(Path.GetFileName).Order());
    }

    [Theory]
    [InlineData("locked-rar4.rar")]
    [InlineData("locked-rar4-headers.rar")]
    [InlineData("locked-rar5.rar")]
    [InlineData("locked-rar5-headers.rar")]
    public async Task Password_protected_rar_fails_without_an_iso(string fixture)
    {
        Assert.SkipUnless(SevenZipExe is not null, "Bundled 7-Zip required");
        var archive = Path.Combine(fx.NewFolder(), fixture);
        File.Copy(Fixture(fixture), archive);
        await AssertLockedAsync(archive);
        Assert.Single(Directory.GetFileSystemEntries(Path.GetDirectoryName(archive)!));
    }

    [Theory]
    [InlineData("alz")]
    [InlineData("egg")]
    public async Task Encrypted_builtin_formats_fail_without_an_iso(string format)
    {
        Assert.SkipUnless(SevenZipExe is not null, "Bundled 7-Zip required");
        var archive = Path.Combine(fx.NewFolder(), "locked." + format);
        if (format == "alz")
            WriteAlz(archive, [new AlzItem("secret.txt", "secret"u8.ToArray(), Encrypted: true)]);
        else
        {
            using var writer = new BinaryWriter(File.Create(archive));
            writer.Write(0x41474745u);
            writer.Write((ushort)0x0100);
            writer.Write(1u);
            writer.Write(0u);
            writer.Write(0x08D1470Fu);
            writer.Write((byte)0);
        }
        await AssertLockedAsync(archive);
        Assert.Single(Directory.GetFileSystemEntries(Path.GetDirectoryName(archive)!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invalid_archives_do_not_leave_iso_or_staging_files(bool traversal)
    {
        Assert.SkipUnless(SevenZipExe is not null, "Bundled 7-Zip required");
        var dir = fx.NewFolder();
        var source = Path.Combine(dir, "invalid.zip");
        if (!traversal)
            await File.WriteAllTextAsync(source, "not a zip", Ct);
        else
        {
            using var zip = ZipFile.Open(source, ZipArchiveMode.Create);
            using var writer = new StreamWriter(zip.CreateEntry("../../escape.txt").Open());
            writer.Write("escape");
        }
        var job = await fx.ConvertAsync([source], new Preset { TargetFormat = "iso" });
        Assert.Equal(JobState.Failed, job.State);
        Assert.Empty(job.Outputs);
        Assert.Equal(["invalid.zip"], Directory.GetFileSystemEntries(dir).Select(Path.GetFileName));
    }

    private async Task AssertLockedAsync(string archive)
    {
        var job = await fx.ConvertAsync([archive], new Preset { TargetFormat = "iso" });
        Assert.Equal(JobState.Failed, job.State);
        Assert.Contains("password-protected", job.Files[0].ErrorDetail);
        Assert.Empty(job.Outputs);
    }

    [Theory]
    [InlineData(ConflictPolicy.Rename)]
    [InlineData(ConflictPolicy.Skip)]
    [InlineData(ConflictPolicy.Overwrite)]
    public async Task Iso_conversion_honours_output_conflicts(ConflictPolicy conflict)
    {
        Assert.SkipUnless(SevenZipExe is not null, "Bundled 7-Zip required");
        var dir = fx.NewFolder();
        var source = await CreateSourceAsync(dir, "zip");
        var destination = Path.Combine(dir, "source.iso");
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
            Assert.Equal(Path.Combine(dir, conflict == ConflictPolicy.Rename ? "source (2).iso" : "source.iso"), output);
            using var image = File.OpenRead(output);
            using var reader = new CDReader(image, true);
            Assert.True(reader.FileExists("readme.txt"));
        }
        if (conflict != ConflictPolicy.Overwrite)
            Assert.Equal("existing", await File.ReadAllTextAsync(destination, Ct));
    }

    [Fact]
    public async Task Empty_zip_becomes_a_valid_empty_iso()
    {
        Assert.SkipUnless(SevenZipExe is not null, "Bundled 7-Zip required");
        var source = Path.Combine(fx.NewFolder(), "empty.zip");
        using (ZipFile.Open(source, ZipArchiveMode.Create)) { }
        var job = await fx.ConvertAsync([source], new Preset { TargetFormat = "iso" });
        Assert.Equal(JobState.Completed, job.State);
        using var image = File.OpenRead(Assert.Single(job.Outputs));
        using var reader = new CDReader(image, true);
        Assert.Empty(reader.GetFileSystemEntries("\\"));
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Archives", name);
    private static string SourceFormat(string format) => format switch { "wbpz" => "zip", "rar5" => "rar", _ => format };

    private static async Task<string> CreateSourceAsync(string dir, string format)
    {
        var tree = MakeTree(dir, "tree");
        var source = Path.Combine(dir, "source." + (format == "wbpz" ? "wbpz" : FormatRegistry.Get(SourceFormat(format)).PrimaryExtension));
        var fixture = format switch
        {
            "rar" => "sample.rar",
            "rar5" => "sample-rar5.rar",
            "cab" => "sample.cab",
            "lha" => "sample.lzh",
            "rpm" => "sample.rpm",
            "arj" => "sample.arj",
            "dmg" => "sample.dmg",
            "z" or "tz" => "sample.tar.Z",
            _ => null,
        };
        if (fixture is not null)
        {
            File.Copy(Fixture(fixture), source);
            return source;
        }
        switch (format)
        {
            case "zip":
            case "jar":
            case "wbpz":
                ZipFile.CreateFromDirectory(tree, source, CompressionLevel.Optimal, false, Encoding.UTF8);
                break;
            case "7z":
            case "tar":
            case "tgz":
            case "tbz2":
            case "txz":
                await new ArchivePacker(SevenZip.Locate()).PackAsync(Entries(tree), source, format, ArchiveLevel.Normal, dir, null, Ct);
                break;
            case "tar7z":
            case "tlz":
                var tar = Path.Combine(dir, "payload.tar");
                await new ArchivePacker(null).PackAsync(Entries(tree), tar, "tar", ArchiveLevel.Normal, dir, null, Ct);
                if (format == "tar7z")
                    await Run7zAsync("a", "-t7z", source, tar);
                else
                    await File.WriteAllBytesAsync(source, await LzipAsync(await File.ReadAllBytesAsync(tar, Ct), dir), Ct);
                break;
            case "gz":
            case "bz2":
            case "xz":
            case "lzma":
            case "lz":
                var data = Path.Combine(dir, "data.bin");
                await File.WriteAllBytesAsync(data, Noise(8192, 9), Ct);
                if (format == "lz")
                    await File.WriteAllBytesAsync(source, await LzipAsync(await File.ReadAllBytesAsync(data, Ct), dir), Ct);
                else if (format == "lzma")
                {
                    var raw = await RawLzmaAsync(await File.ReadAllBytesAsync(data, Ct), true, dir);
                    using var writer = new BinaryWriter(File.Create(source));
                    writer.Write((byte)0x5d);
                    writer.Write(1 << 20);
                    writer.Write(-1L);
                    writer.Write(raw);
                }
                else
                    await Run7zAsync("a", "-t" + (format == "gz" ? "gzip" : format == "bz2" ? "bzip2" : format), source, data);
                break;
            case "alz":
                WriteAlz(source, [new AlzItem("empty", null), new AlzItem("한글.txt", "hello"u8.ToArray(), 2)]);
                break;
            case "egg":
                WriteEgg(source, [new EggItem("empty", true), new EggItem("한글.txt", false, new EggBlock("hello"u8.ToArray(), 1))]);
                break;
            case "img":
                var rawFolder = Path.Combine(dir, "raw");
                await Run7zAsync("x", "-tDmg", "-y", "-o" + rawFolder, Fixture("sample.dmg"));
                File.Copy(Assert.Single(Directory.GetFiles(rawFolder, "*.DOS_FAT_32")), source);
                break;
            case "iso":
                var builder = new CDBuilder { UseJoliet = true, VolumeIdentifier = "TEST" };
                builder.AddDirectory("empty");
                builder.AddFile("한글.txt", "hello"u8.ToArray());
                builder.Build(source);
                break;
            case "cpio":
                WriteCpio(source);
                break;
            case "deb":
                var payload = Path.Combine(dir, "data.tar.gz");
                await new ArchivePacker(null).PackAsync(Entries(tree), payload, "tgz", ArchiveLevel.Normal, dir, null, Ct);
                using (var output = File.Create(source))
                {
                    output.Write("!<arch>\n"u8);
                    WriteArEntry(output, "debian-binary", "2.0\n"u8.ToArray());
                    WriteArEntry(output, "data.tar.gz", await File.ReadAllBytesAsync(payload, Ct));
                }
                break;
            default:
                throw new InvalidOperationException("Missing genuine fixture for " + format);
        }
        return source;
    }

    private static List<PackItem> Entries(string tree) => Directory.EnumerateFileSystemEntries(tree).Select(p => new PackItem(p, Path.GetFileName(p))).ToList();

    private static void WriteArEntry(Stream stream, string name, byte[] bytes)
    {
        var header = $"{(name + "/").PadRight(16)}{0.ToString().PadRight(12)}{0.ToString().PadRight(6)}{0.ToString().PadRight(6)}{"100644".PadRight(8)}{bytes.Length.ToString().PadRight(10)}`\n";
        stream.Write(Encoding.ASCII.GetBytes(header));
        stream.Write(bytes);
        if (bytes.Length % 2 != 0)
            stream.WriteByte((byte)'\n');
    }

    private static void WriteCpio(string path)
    {
        using var stream = File.Create(path);
        foreach (var (name, data) in new[] { ("data.txt", "hello"u8.ToArray()), ("TRAILER!!!", Array.Empty<byte>()) })
        {
            var header = "070701" + string.Concat(new uint[] { 1, 0x81a4, 0, 0, 1, 0, (uint)data.Length, 0, 0, 0, 0, (uint)name.Length + 1, 0 }.Select(n => n.ToString("x8")));
            stream.Write(Encoding.ASCII.GetBytes(header + name + '\0'));
            while (stream.Position % 4 != 0)
                stream.WriteByte(0);
            stream.Write(data);
            while (stream.Position % 4 != 0)
                stream.WriteByte(0);
        }
    }
}
