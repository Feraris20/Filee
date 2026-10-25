// ISO choices survive saving and cloning while old and future presets retain the Unicode-safe default.

using System.Text.Json;
using Filee.Core.Presets;
using Filee.Core.Settings;

namespace Filee.Core.Tests;

public class IsoOptionsTests
{
    [Fact]
    public void Old_presets_and_unknown_standards_default_to_joliet()
    {
        Assert.Equal(IsoStandard.Iso9660Joliet, JsonSerializer.Deserialize<Preset>("{}")!.Iso.Standard);
        Assert.Equal(IsoStandard.Iso9660Joliet, JsonSerializer.Deserialize<IsoOptions>("""{"Standard":"FutureStandard"}""")!.Standard);
    }

    [Theory]
    [InlineData(IsoStandard.Iso9660)]
    [InlineData(IsoStandard.Iso9660Joliet)]
    [InlineData(IsoStandard.Udf102)]
    public void Saving_and_cloning_preserve_the_iso_standard_independently(IsoStandard standard)
    {
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path);
        store.Load();
        var preset = store.FindPreset("to-iso")!;
        preset.Iso.Standard = standard;
        var clone = preset.Clone();
        clone.Iso.Standard = standard == IsoStandard.Iso9660Joliet ? IsoStandard.Iso9660 : IsoStandard.Iso9660Joliet;
        Assert.Equal(standard, preset.Iso.Standard);
        store.SaveLibrary();
        var loaded = new UserDataStore(dir.Path);
        loaded.Load();
        Assert.Equal(standard, loaded.FindPreset("to-iso")!.Iso.Standard);
    }

    [Fact]
    public void A_saved_combine_flag_does_not_turn_iso_into_archive_compression()
    {
        Assert.False(new Preset { TargetFormat = "iso", Archive = { CombineIntoOne = true } }.PacksIntoOneArchive);
        Assert.True(new Preset { TargetFormat = "zip", Archive = { CombineIntoOne = true } }.PacksIntoOneArchive);
    }
}
