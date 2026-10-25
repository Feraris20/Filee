// Options for data ISO images, separate from archive compression.

using System.Text.Json.Serialization;

namespace Filee.Core.Presets;

/// <summary>Filesystems the ISO writer can create.</summary>
public enum IsoStandard
{
    /// <summary>ISO 9660 with Joliet names, preserving Unicode. The default for existing presets.</summary>
    Iso9660Joliet,
    /// <summary>Plain ISO 9660 with legacy uppercase ASCII names.</summary>
    Iso9660,
    /// <summary>UDF 1.02 with an ISO 9660 bridge, created by bundled mkisofs on every platform.</summary>
    Udf102,
}

/// <summary>Options for ISO creation from archives or folders.</summary>
public sealed class IsoOptions
{
    /// <summary>Filesystem to create. Joliet is used when an older preset has no ISO options.</summary>
    [JsonConverter(typeof(LenientEnumJsonConverter<IsoStandard>))]
    public IsoStandard Standard { get; set; } = IsoStandard.Iso9660Joliet;

    /// <summary>Copies the options so editing a cloned preset leaves the original alone.</summary>
    public IsoOptions Clone() => (IsoOptions)MemberwiseClone();
}
