// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Buffers.Binary;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// Reads the EXIF orientation an encoded image declares from its header alone
/// </summary>
public static class ExifOrientation
{
    /// <summary>
    /// The TIFF tag holding orientation.
    /// </summary>
    private const ushort orientation_tag = 0x0112;

    /// <summary>
    /// Whether <paramref name="encoded"/> declares an orientation that would rotate or flip it, i.e.
    /// whether a decoder that ignores EXIF would produce a visibly wrong result.
    /// </summary>
    public static bool RequiresTransform(ReadOnlySpan<byte> encoded)
    {
        // 1 is TopLeft, i.e. already upright. 0 is not a legal value but does occur in the wild, and
        // treating it as upright is what ImageSharp's AutoOrient does with it.
        int orientation = Read(encoded);

        return orientation is not (0 or 1);
    }

    /// <summary>
    /// The declared orientation (1-8), or 0 when the image carries none.
    /// </summary>
    public static int Read(ReadOnlySpan<byte> encoded)
    {
        if (encoded.Length < 4)
            return 0;

        // JPEG: SOI, then EXIF rides in an APP1 segment.
        if (encoded[0] == 0xFF && encoded[1] == 0xD8)
            return readFromJpeg(encoded);

        // PNG: EXIF rides in an eXIf chunk, whose payload is a bare TIFF block.
        ReadOnlySpan<byte> pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        if (encoded.Length >= 8 && encoded[..8].SequenceEqual(pngSignature))
            return readFromPng(encoded);

        return 0;
    }

    private static int readFromJpeg(ReadOnlySpan<byte> encoded)
    {
        ReadOnlySpan<byte> exifMarker = "Exif\0\0"u8;

        int position = 2;

        while (position + 4 <= encoded.Length)
        {
            // Segments are introduced by 0xFF. A run of them is padding, not a malformed file.
            if (encoded[position] != 0xFF)
                return 0;

            while (position < encoded.Length && encoded[position] == 0xFF)
                position++;

            if (position >= encoded.Length)
                return 0;

            byte marker = encoded[position++];

            switch (marker)
            {
                // SOS begins entropy-coded data and SOI/EOI carry no payload — either way there is no
                // further metadata to find, and scanning into compressed bytes would only find noise.
                case 0xDA:
                case 0xD9:
                    return 0;
                // Standalone markers: no length field follows.
                case 0x01:
                case >= 0xD0 and <= 0xD8:
                    continue;
            }

            if (position + 2 > encoded.Length)
                return 0;

            // The length includes its own two bytes.
            int length = BinaryPrimitives.ReadUInt16BigEndian(encoded[position..]);

            if (length < 2 || position + length > encoded.Length)
                return 0;

            var payload = encoded.Slice(position + 2, length - 2);

            if (marker == 0xE1 && payload.Length > exifMarker.Length && payload[..exifMarker.Length].SequenceEqual(exifMarker))
                return readFromTiff(payload[exifMarker.Length..]);

            position += length;
        }

        return 0;
    }

    private static int readFromPng(ReadOnlySpan<byte> encoded)
    {
        int position = 8;

        while (position + 8 <= encoded.Length)
        {
            uint length = BinaryPrimitives.ReadUInt32BigEndian(encoded[position..]);
            var type = encoded.Slice(position + 4, 4);

            // Past IDAT there is nothing worth walking megabytes of compressed data for; an eXIf chunk
            // is permitted after it, so this trades a rare miss for not scanning the whole file.
            if (type.SequenceEqual("IDAT"u8) || type.SequenceEqual("IEND"u8))
                return 0;

            if (length > int.MaxValue || position + 12 + (int)length > encoded.Length)
                return 0;

            if (type.SequenceEqual("eXIf"u8))
                return readFromTiff(encoded.Slice(position + 8, (int)length));

            // length + the 4-byte length, 4-byte type and 4-byte CRC
            position += 12 + (int)length;
        }

        return 0;
    }

    /// <summary>
    /// Walks IFD0 of a TIFF block for <see cref="orientation_tag"/>. Shared because a JPEG's APP1
    /// payload and a PNG's eXIf chunk hold the same structure.
    /// </summary>
    private static int readFromTiff(ReadOnlySpan<byte> tiff)
    {
        if (tiff.Length < 8)
            return 0;

        bool littleEndian;

        if (tiff[0] == 0x49 && tiff[1] == 0x49)
            littleEndian = true;
        else if (tiff[0] == 0x4D && tiff[1] == 0x4D)
            littleEndian = false;
        else
            return 0;

        if (read16(tiff[2..], littleEndian) != 42)
            return 0;

        uint directory = read32(tiff[4..], littleEndian);

        if (directory + 2 > (uint)tiff.Length)
            return 0;

        int entries = read16(tiff[(int)directory..], littleEndian);

        for (int i = 0; i < entries; i++)
        {
            int entry = (int)directory + 2 + i * 12;

            if (entry + 12 > tiff.Length)
                return 0;

            if (read16(tiff[entry..], littleEndian) != orientation_tag)
                continue;

            // Orientation is a SHORT, so the value sits in the first two bytes of the value field
            // rather than being an offset to it.
            return read16(tiff[(entry + 8)..], littleEndian);
        }

        return 0;
    }

    private static ushort read16(ReadOnlySpan<byte> source, bool littleEndian)
        => littleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(source) : BinaryPrimitives.ReadUInt16BigEndian(source);

    private static uint read32(ReadOnlySpan<byte> source, bool littleEndian)
        => littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(source) : BinaryPrimitives.ReadUInt32BigEndian(source);
}
