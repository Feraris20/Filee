// ISO presets show filesystem selection, persist it, and hide archive compression in the actual editor.

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Filee.App.Services;
using Filee.App.ViewModels;
using Filee.App.Views;
using Filee.Core.Localization;
using Filee.Core.Presets;

namespace Filee.App.Tests;

public class IsoPresetEditorTests
{
    [AvaloniaFact]
    public void Iso_editor_shows_supported_standards_instead_of_archive_compression()
    {
        TestServices.EnsureInitialized("en");
        var preset = new Preset { Name = "ISO", TargetFormat = "iso", Archive = { CombineIntoOne = true } };
        var vm = new PresetEditorViewModel(preset, AppHost.Get<ILocalizer>());
        Assert.True(vm.ShowIso);
        Assert.False(vm.ShowArchive || vm.ShowArchiveOptions);
        Assert.Equal(Enum.GetValues<IsoStandard>().Order(), vm.IsoStandards.Select(s => s.Value).Order());
        Assert.Equal(IsoStandard.Iso9660Joliet, vm.IsoStandardChoice.Value);
        var window = new PresetEditorWindow { DataContext = vm, Height = 900 };
        window.Show();
        try
        {
            RenderTests.Pump();
            var view = window.GetVisualDescendants().OfType<PresetEditorView>().Single();
            Assert.True(view.FindControl<Border>("IsoCard")!.IsVisible);
            Assert.False(view.FindControl<Border>("ArchiveCard")!.IsVisible);
            var selector = view.FindControl<ComboBox>("IsoStandardSelector")!;
            selector.SelectedItem = vm.IsoStandards.Single(s => s.Value == IsoStandard.Iso9660);
            Assert.True(vm.Apply());
            Assert.Equal(IsoStandard.Iso9660, preset.Iso.Standard);
            Assert.False(preset.Archive.CombineIntoOne);
            Assert.Equal(IsoStandard.Iso9660, new PresetEditorViewModel(preset, AppHost.Get<ILocalizer>()).IsoStandardChoice.Value);
            RenderTests.Save(window, "preset-iso-standards.png");
            selector.SelectedItem = vm.IsoStandards.Single(s => s.Value == IsoStandard.Udf102);
            RenderTests.Pump();
            Assert.True(vm.ShowUdfHint);
            Assert.True(vm.Apply());
            Assert.Equal(IsoStandard.Udf102, preset.Iso.Standard);
            RenderTests.Save(window, "preset-iso-udf.png");
            vm.Target = vm.Targets.Single(t => t.Value == "zip");
            RenderTests.Pump();
            Assert.False(vm.ShowIso);
            Assert.True(vm.ShowArchiveOptions);
            Assert.False(view.FindControl<Border>("IsoCard")!.IsVisible);
            Assert.True(view.FindControl<Border>("ArchiveCard")!.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }
}
