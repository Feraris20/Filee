// Directories are inputs of their own even when named with a supported extension; their full names survive output.

using Filee.Core.Formats;
using Filee.Core.Presets;
using Filee.Core.Profiles;

namespace Filee.Core.Tests;

public class FolderIsoFormatTests
{
    [Theory]
    [InlineData("photos")]
    [InlineData("photos.jpg")]
    [InlineData("backup.tar.gz")]
    public void Folder_detection_profile_and_output_keep_the_entire_name(string name)
    {
        using var dir = new TempDir();
        var folder = Directory.CreateDirectory(Path.Combine(dir.Path, name)).FullName;
        Assert.Equal(FormatRegistry.Folder, FormatRegistry.Detect(folder)?.Id);
        Assert.Equal("", FormatRegistry.ExtensionOf(folder));
        Assert.Equal(name, FormatRegistry.NameWithoutExtension(folder + Path.DirectorySeparatorChar));
        Assert.Equal("archives", ProfileSelector.Select(BuiltInData.CreateProfiles(), [folder])?.Id);
        Assert.Equal([FormatRegistry.Get(FormatRegistry.Folder)], ProfileSelector.DetectFormats([folder]));
        var extensionless = Path.Combine(dir.Path, "LICENSE");
        File.WriteAllText(extensionless, "plain file");
        Assert.Equal("mixed", ProfileSelector.Select(BuiltInData.CreateProfiles(), [extensionless])?.Id);
        var output = OutputPathResolver.Resolve(new OutputRule(), new(folder, "ISO", 1, DateTime.Now), "iso", _ => false);
        Assert.Equal(folder + ".iso", output);
    }
}
