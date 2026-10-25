// Writes folder contents as a data ISO without an external engine. Folder input is a direct-only route.

using Filee.Core.Conversion;
using Filee.Core.Formats;

namespace Filee.Engines.Archives;

/// <summary>Creates an ISO 9660/Joliet image from the contents of a folder.</summary>
public sealed class FolderIsoConverter : IConverter
{
    public string Id => "iso";
    public string DisplayName => "ISO images";
    public int MaxParallelism => 2;
    public IReadOnlyList<ConversionEdge> Edges { get; } = [new(FormatRegistry.Folder, "iso", DirectOnly: true)];
    public EngineStatus GetStatus() => EngineStatus.Available(version: "DiscUtils 0.16.13");

    public async Task<IReadOnlyList<string>> ConvertAsync(ConversionStep step, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var source = Path.GetFullPath(Path.TrimEndingDirectorySeparator(step.InputPath));
        var folder = new DirectoryInfo(source);
        if (!folder.Exists)
            throw new DirectoryNotFoundException($"The folder '{source}' no longer exists.");
        if (folder.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new NotSupportedException("Filee cannot create an ISO from a linked folder. Select the original folder instead.");

        var output = step.Output.Allocate("iso");
        if (output is null)
            return [];
        // The selected folder becomes the disc root; include its entries rather than nesting its name again.
        var items = folder.EnumerateFileSystemInfos()
            .Where(info => !info.Attributes.HasFlag(FileAttributes.ReparsePoint)
                           && !Filee.Core.Platform.FileSystemPaths.Comparer.Equals(info.FullName, Path.GetFullPath(output)))
            .OrderBy(info => info.Name, StringComparer.Ordinal)
            .Select(info => new PackItem(info.FullName, info.Name)).ToList();
        await new ArchivePacker(null).PackAsync(items, output, "iso", step.Preset.Archive.Level,
            step.WorkDirectory, progress, cancellationToken);
        progress?.Report(1);
        return [output];
    }
}
