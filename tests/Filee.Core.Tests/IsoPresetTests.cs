// ISO presets are offered once during upgrade, preserving customised toolbars and presets.

using Filee.Core.Formats;
using Filee.Core.Presets;
using Filee.Core.Profiles;
using Filee.Core.Settings;

namespace Filee.Core.Tests;

public class IsoPresetTests
{
    [Fact]
    public void Iso_is_writable_and_offered_in_new_archive_toolbars()
    {
        Assert.True(FormatRegistry.Get("iso").Writable);
        Assert.Equal("iso", BuiltInData.CreatePresets().Single(p => p.Id == "to-iso").TargetFormat);
        Assert.Contains("to-iso", BuiltInData.CreateProfiles().Single(p => p.Id == "archives").PresetIds);
    }

    [Fact]
    public void Upgrade_offers_iso_once()
    {
        var presets = BuiltInData.CreatePresets();
        var profiles = BuiltInData.CreateProfiles();
        presets.RemoveAll(p => p.Id == "to-iso");
        profiles.Single(p => p.Id == "archives").PresetIds.Remove("to-iso");
        Assert.True(SettingsMigrations.ApplyToLibrary(46, presets, profiles));
        Assert.Single(presets, p => p.Id == "to-iso");
        Assert.Contains("to-iso", profiles.Single(p => p.Id == "archives").PresetIds);
        Assert.False(SettingsMigrations.ApplyToLibrary(46, presets, profiles));
    }

    [Fact]
    public void Upgrade_preserves_custom_presets_and_full_toolbars()
    {
        var presets = BuiltInData.CreatePresets();
        presets.Single(p => p.Id == "to-iso").Name = "My disc";
        var profiles = BuiltInData.CreateProfiles();
        var archive = profiles.Single(p => p.Id == "archives");
        archive.PresetIds = Enumerable.Range(0, ToolbarProfile.MaxSlices).Select(i => "custom-" + i).ToList();
        var expected = archive.PresetIds.ToArray();
        SettingsMigrations.ApplyToLibrary(46, presets, profiles);
        Assert.Equal(expected, archive.PresetIds);
        Assert.Equal("My disc", presets.Single(p => p.Id == "to-iso").Name);
    }
}
