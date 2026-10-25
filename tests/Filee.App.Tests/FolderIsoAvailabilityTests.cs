// Folder ISO is offered by the existing archive toolbar with only the built-in ISO engine installed.

using Avalonia.Headless.XUnit;
using Filee.App.Services;
using Filee.Core.Formats;
using Filee.Core.Presets;
using Filee.Engines.Archives;

namespace Filee.App.Tests;

public class FolderIsoAvailabilityTests
{
    [AvaloniaFact]
    public void Folder_iso_is_available_without_an_external_engine()
    {
        using var fixture = new PresetAvailabilityTests.Fixture(new FolderIsoConverter());
        var preset = BuiltInData.CreatePresets().Single(p => p.Id == "to-iso");
        Assert.Null(fixture.Availability.Check(preset, [FormatRegistry.Get(FormatRegistry.Folder)]));
        Assert.Equal(PresetUse.Usable, fixture.Availability.ForPalette([FormatRegistry.Get(FormatRegistry.Folder)]).Judge(preset).Use);
        Assert.NotNull(fixture.Availability.Check(new Preset { TargetFormat = "jpg" }, [FormatRegistry.Get(FormatRegistry.Folder)]));
    }
}
