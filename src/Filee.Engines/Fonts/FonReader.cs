using System.Buffers.Binary;
using System.Text;

namespace Filee.Engines.Fonts;

/// <summary>A validated raster glyph; pixels are indexed by row, then column.</summary>
internal sealed record FonGlyph(byte ByteValue, int Width, bool[,] Pixels);

/// <summary>One RT_FONT resource, independent of its NE container.</summary>
internal sealed record FonStrike(
    int Version, int PixelWidth, int PixelHeight, int Ascent, int ExternalLeading,
    bool Italic, int Weight, byte CharSet, string Face, int DefaultChar, int BreakChar,
    IReadOnlyList<FonGlyph> Glyphs);

/// <summary>Reads the monochrome FNT v2/v3 resources of a 16-bit NE font library.</summary>
internal static class FonReader
{
    /// <summary>
    /// Maximum sum of max(width, 1) * height for every glyph entry across all RT_FONT resources;
    /// zero-width rows and entries aliasing the same bitmap bytes each count separately.
    /// </summary>
    internal const int MaxGlyphPixels = 4_194_304;

    public static IReadOnlyList<FonStrike> Read(byte[] data) => Read(data, CancellationToken.None);

    /// <summary>
    /// Validates every glyph entry's max(width, 1) * height across all RT_FONT resources before decoding
    /// (aliases count per entry), observing <paramref name="cancellationToken"/> at each glyph during preflight and decoding.
    /// </summary>
    public static IReadOnlyList<FonStrike> Read(byte[] data, CancellationToken cancellationToken)
    {
        if (data.Length < 0x40 || data[0] != 'M' || data[1] != 'Z')
            throw new InvalidDataException("FON requires an MZ executable header.");

        var ne = (long)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x3c));
        if (!Fits(data.Length, ne, 0x28))
            throw new InvalidDataException("FON has a truncated NE header.");
        if (data[ne] == 'P' && data[ne + 1] == 'E')
            throw new NotSupportedException("PE FON resources are not supported; an NE font is required.");
        if (data[ne] != 'N' || data[ne + 1] != 'E')
            throw new InvalidDataException("FON has no NE executable header.");

        var table = ne + U16(data, ne + 0x24);
        var end = ne + U16(data, ne + 0x26);
        if (end <= table || !Fits(data.Length, table, 2) || end > data.Length)
            throw new InvalidDataException("FON has an invalid NE resource table.");
        var shift = U16(data, table);
        if (shift > 15)
            throw new InvalidDataException("FON has an invalid NE resource alignment shift.");

        var ranges = new List<(long Start, long End)>();
        var cursor = table + 2;
        var terminated = false;
        while (Fits(end, cursor, 2))
        {
            var type = U16(data, cursor);
            if (type == 0)
            {
                terminated = true;
                break;
            }
            if (!Fits(end, cursor, 8))
                throw new InvalidDataException("FON has a truncated NE resource type.");
            var count = U16(data, cursor + 2);
            cursor += 8;
            if (!Fits(end, cursor, (long)count * 12))
                throw new InvalidDataException("FON has a truncated NE resource name table.");
            if (type == 0x8008)
            {
                for (var i = 0; i < count; i++)
                {
                    var entry = cursor + (long)i * 12;
                    var start = (long)U16(data, entry) << shift;
                    var length = (long)U16(data, entry + 2) << shift;
                    if (length == 0 || start < end || !Fits(data.Length, start, length))
                        throw new InvalidDataException("FON has an overlapping or out-of-bounds RT_FONT resource.");
                    ranges.Add((start, start + length));
                }
            }
            cursor += (long)count * 12;
        }
        if (!terminated)
            throw new InvalidDataException("FON has an unterminated NE resource table.");
        if (ranges.Count == 0)
            throw new InvalidDataException("FON contains no RT_FONT resources.");
        var sorted = ranges.OrderBy(r => r.Start).ToArray();
        for (var i = 1; i < sorted.Length; i++)
            if (sorted[i].Start < sorted[i - 1].End)
                throw new InvalidDataException("FON has an overlapping or out-of-bounds RT_FONT resource.");
        long glyphPixels = 0;
        foreach (var (start, stop) in ranges)
            ReadFnt(data.AsSpan((int)start, (int)(stop - start)), false, ref glyphPixels, cancellationToken);
        var strikes = new List<FonStrike>(ranges.Count);
        foreach (var (start, stop) in ranges)
            strikes.Add(ReadFnt(data.AsSpan((int)start, (int)(stop - start)), true, ref glyphPixels, cancellationToken)!);
        return strikes;
    }

    private static FonStrike? ReadFnt(ReadOnlySpan<byte> resource, bool decode, ref long glyphPixels,
        CancellationToken cancellationToken)
    {
        if (resource.Length < 6)
            throw new InvalidDataException("RT_FONT has a truncated FNT header.");
        var version = U16(resource, 0);
        if (version is not (0x0200 or 0x0300))
            throw new NotSupportedException($"FNT version 0x{version:X4} is not supported (only v2/v3).");
        var header = version == 0x0200 ? 0x76 : 0x94;
        var size = (long)U32(resource, 2);
        if (size < header || size > resource.Length)
            throw new InvalidDataException("RT_FONT has an invalid FNT dfSize or truncated header.");
        var fnt = resource[..(int)size];
        var type = U16(fnt, 0x42);
        if ((type & 1) != 0)
            throw new NotSupportedException("Vector FNT fonts are not supported.");
        if ((type & 4) != 0)
            throw new NotSupportedException("FNT bitmap pixels stored outside the file are not supported.");
        if (version == 0x0300)
        {
            var flags = U32(fnt, 0x76);
            if (flags is not (1 or 2))
                throw new NotSupportedException($"FNT v3 dfFlags 0x{flags:X8} uses an unsupported glyph table or color format.");
        }

        var height = U16(fnt, 0x58);
        var ascent = U16(fnt, 0x4a);
        var first = fnt[0x5f];
        var last = fnt[0x60];
        var count = last - first + 1;
        if (height == 0 || height > 16384 || ascent > height || count <= 0)
            throw new InvalidDataException("FNT has an invalid height, ascent or character range.");
        var defaultChar = fnt[0x61];
        var breakChar = fnt[0x62];
        if (defaultChar >= count || breakChar >= count)
            throw new InvalidDataException("FNT default/break characters must be relative indices within the glyph range.");
        var weight = U16(fnt, 0x53);
        if (weight > 1000)
            throw new InvalidDataException("FNT weight is outside the supported 0..1000 range.");
        var charset = fnt[0x55];
        var encoding = CharSetEncoding(charset);
        var faceOffset = (long)U32(fnt, 0x69);
        if (!Fits(size, faceOffset, 1))
            throw new InvalidDataException("FNT face offset is outside the resource.");
        var faceEnd = fnt[(int)faceOffset..].IndexOf((byte)0);
        if (faceEnd < 0)
            throw new InvalidDataException("FNT face name is not NUL terminated.");
        string face;
        try
        {
            face = encoding.GetString(fnt.Slice((int)faceOffset, faceEnd)).Trim();
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException("FNT face name cannot be decoded with its character set.", ex);
        }
        if (face.Length == 0)
            throw new InvalidDataException("FNT face name is empty.");

        var entrySize = version == 0x0200 ? 4 : 6;
        var tableEnd = header + (long)(count + 1) * entrySize;
        if (tableEnd > size)
            throw new InvalidDataException("FNT character table or sentinel is truncated.");
        var bitsOffset = (long)U32(fnt, 0x71);
        if (bitsOffset < tableEnd || bitsOffset > size)
            throw new InvalidDataException("FNT bitmap region offset is outside the resource.");
        var glyphs = decode ? new List<FonGlyph>(count) : null;
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = header + i * entrySize;
            var width = U16(fnt, entry);
            var offset = version == 0x0200 ? U16(fnt, entry + 2) : U32(fnt, entry + 2);
            var bytes = ((long)width + 7) / 8 * height;
            if (offset < bitsOffset || !Fits(size, offset, bytes))
                throw new InvalidDataException($"FNT bitmap for character {first + i} is outside the resource.");
            if (!decode)
            {
                glyphPixels += (long)Math.Max((int)width, 1) * height;
                if (glyphPixels > MaxGlyphPixels)
                    throw new InvalidDataException($"FON glyph bitmap rows/pixels exceed the {MaxGlyphPixels:N0}-unit limit.");
                continue;
            }
            var pixels = new bool[height, width];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                    pixels[y, x] = (fnt[(int)(offset + (long)(x / 8) * height + y)] & (0x80 >> (x % 8))) != 0;
            glyphs!.Add(new FonGlyph((byte)(first + i), width, pixels));
        }
        if (glyphs is null)
            return null;
        return new FonStrike(version, U16(fnt, 0x56), height, ascent, U16(fnt, 0x4e),
            fnt[0x50] != 0, weight == 0 ? 400 : weight, charset, face, defaultChar, breakChar, glyphs);
    }

    /// <summary>The strict Windows code page encoding of a supported single-byte FNT charset.</summary>
    internal static Encoding CharSetEncoding(byte charset)
    {
        var codePage = charset switch
        {
            0 => 1252,
            238 => 1250,
            204 => 1251,
            161 => 1253,
            162 => 1254,
            177 => 1255,
            178 => 1256,
            186 => 1257,
            163 => 1258,
            222 => 874,
            _ => throw new NotSupportedException($"FNT character set {charset} is not a supported single-byte Windows charset.")
        };
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    private static bool Fits(long limit, long start, long length) =>
        start >= 0 && length >= 0 && start <= limit && length <= limit - start;

    private static ushort U16(ReadOnlySpan<byte> data, long offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data.Slice((int)offset, 2));

    private static uint U32(ReadOnlySpan<byte> data, long offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data.Slice((int)offset, 4));
}
