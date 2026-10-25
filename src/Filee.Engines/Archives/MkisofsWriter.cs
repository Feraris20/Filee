// UDF 1.02/ISO bridge images with Filee's bundled mkisofs. Explicit grafts preserve names without copying file data.

using System.Text;
using Filee.Engines.Infrastructure;

namespace Filee.Engines.Archives;

/// <summary>One explicit file or empty-directory graft in a UDF image.</summary>
internal sealed record UdfItem(string Path, string Name, bool IsFolder, long Length, DateTimeOffset Modified);

/// <summary>Writes UDF images using the same separate-process engine on every platform.</summary>
internal static class MkisofsWriter
{
    /// <summary>Filee's pinned copy; never an arbitrary program from PATH.</summary>
    internal static string? Locate() => EngineEnvironment.FindBundled("mkisofs") is { } folder
        ? EngineEnvironment.FirstExisting(System.IO.Path.Combine(folder, OperatingSystem.IsWindows() ? "mkisofs.exe" : "mkisofs"))
        : null;

    internal static async Task WriteAsync(IEnumerable<UdfItem> items, string temporaryOutput, string destination,
        string workDirectory, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var executable = Locate() ?? throw new InvalidOperationException(
            "UDF creation needs Filee's bundled mkisofs engine (engines/mkisofs). Run build/build-mkisofs.ps1 to prepare it.");
        var scratch = System.IO.Path.Combine(workDirectory, "iso-udf-" + Guid.NewGuid().ToString("N")[..8]);
        var emptyRoot = System.IO.Path.Combine(scratch, "root");
        Directory.CreateDirectory(emptyRoot);
        try
        {
            var list = System.IO.Path.Combine(scratch, "grafts.txt");
            long bytes = 0;
            var index = 0;
            var count = 0;
            using (var writer = new StreamWriter(list, false, new UTF8Encoding(false)))
            {
                // The Windows engine uses Cygwin; CRLF would make the trailing CR part of every source path.
                writer.NewLine = "\n";
                foreach (var item in items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (Filee.Core.Platform.FileSystemPaths.Comparer.Equals(System.IO.Path.GetFullPath(item.Path), System.IO.Path.GetFullPath(destination)))
                        continue;
                    ValidateName(item.Name);
                    count++;
                    var source = item.Path;
                    if (item.IsFolder)
                    {
                        // Directory grafts must be empty: recursively passing a source folder would also include links.
                        source = System.IO.Path.Combine(scratch, "d" + index++);
                        Directory.CreateDirectory(source);
                        Directory.SetLastWriteTimeUtc(source, item.Modified.UtcDateTime);
                    }
                    else
                    {
                        bytes = checked(bytes + item.Length);
                    }
                    var fullSource = System.IO.Path.GetFullPath(source);
                    if (OperatingSystem.IsWindows())
                        fullSource = fullSource.Replace('\\', '/');
                    writer.WriteLine(Escape(item.Name) + "=" + Escape(fullSource));
                }
            }
            // This revision produces an unreadable UDF root when it has no directory entries at all.
            if (count == 0)
                throw new NotSupportedException("UDF creation requires at least one file or folder. Choose ISO 9660 + Joliet for a completely empty image.");
            // mkisofs always writes an ISO bridge. Level 3 allows multi-extent files beyond 4 GiB.
            using (ProgressEstimate.Start(progress, TimeSpan.FromSeconds(Math.Max(0.5, bytes / 100e6)), 0.05, 0.95))
            {
                var result = await ProcessRunner.RunAsync(executable,
                    ["-quiet", "-UDF", "-iso-level", "3", "-input-charset", "utf-8", "-no-udf-symlinks",
                     "errctl=ABORT|OPEN|READ|WRITE|GROW|SHRINK|NAMETOOLONG *", "-V", "FILEE", "-o", System.IO.Path.GetFullPath(temporaryOutput),
                     "-graft-points", "-path-list", list, emptyRoot],
                    TimeSpan.FromHours(6), cancellationToken,
                    environment: new Dictionary<string, string> { ["LC_ALL"] = "C.UTF-8" });
                if (result.ExitCode != 0 || !File.Exists(temporaryOutput))
                    throw new InvalidOperationException("mkisofs could not create the UDF image. " + result.StandardError.Trim());
            }
        }
        finally
        {
            ArchiveFiles.TryDeleteFolder(scratch);
        }
    }

    private static void ValidateName(string name)
    {
        if (name.Contains('\n') || name.Contains('\r'))
            throw new NotSupportedException("UDF creation does not support filenames containing line breaks.");
        foreach (var part in name.Split('/'))
        {
            // UDF's file identifier is at most 255 bytes, including its compression byte (OSTA CS0).
            var maximum = part.Any(c => c > 255) ? 127 : 254;
            if (part.Length > maximum || part.Any(char.IsSurrogate))
                throw new NotSupportedException($"UDF 1.02 cannot preserve the name '{name}': a name may use at most 254 Latin-1 or 127 Unicode characters, without supplementary characters.");
        }
    }

    private static string Escape(string path)
    {
        if (path.Contains('\n') || path.Contains('\r'))
            throw new NotSupportedException("UDF creation does not support paths containing line breaks.");
        return path.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("=", "\\=", StringComparison.Ordinal);
    }
}
