// Windows bitmap fonts (issue #177): .fon is a read-only font format; a new library's fonts donut takes it.

using Filee.Core.Formats;
using Filee.Core.Presets;
using Filee.Core.Profiles;

namespace Filee.Core.Tests;

public class FonFormatTests
{
    [Theory]
    [InlineData("Fixed.fon")]
    [InlineData("SERIFE.FON")]
    public void Fon_is_input_only(string file)
    {
        var format = FormatRegistry.Detect(file);

        Assert.Equal("fon", format?.Id);
        Assert.Equal(FormatCategory.Font, format!.Category);
        Assert.False(format.Writable);
        Assert.Null(FormatRegistry.Detect("font.fnt"));
    }

    [Fact]
    public void Fon_is_read_only_and_new_font_profile_claims_it()
    {
        var profiles = BuiltInData.CreateProfiles();
        var fonts = ProfileSelector.Select(profiles, ["Fixed.fon"]);

        Assert.Equal("fonts", fonts?.Id);
        Assert.Contains("fon", fonts!.Extensions);
        var targets = fonts.PresetIds.Select(id => BuiltInData.CreatePresets().Single(p => p.Id == id).TargetFormat).Order();
        Assert.Equal(["eot", "otf", "ttf", "woff", "woff2"], targets);
        Assert.All(targets, t => Assert.True(FormatRegistry.Known.Single(f => f.Id == t).Writable));
    }
}
