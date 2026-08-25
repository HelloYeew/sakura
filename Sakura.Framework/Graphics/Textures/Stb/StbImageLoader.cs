// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Buffers;
using System.IO;

namespace Sakura.Framework.Graphics.Textures.Stb;

/// <summary>
/// An <see cref="IImageLoader"/> built on stb_image and stb_image_resize2, through the
/// <c>libsakura-image</c> shim.
/// </summary>
public class StbImageLoader : IImageLoader
{
    /// <summary>
    /// Whether the native library backing this loader is present. False means every <c>Load</c> throws,
    /// so a caller that can fall back should check this rather than catching per image.
    /// </summary>
    public static bool IsAvailable => StbImageNative.IsAvailable;

    public ImageRawData Load(Stream stream) => Load(stream, ImageLoadOptions.FullSize);

    public ImageRawData Load(Stream stream, int maxDimension) => Load(stream, ImageLoadOptions.MaxDimension(maxDimension));

    public ImageRawData Load(Stream stream, ImageLoadOptions options)
    {
        // stb has no streaming entry point: every decoder in it works from one contiguous buffer, so
        // the encoded bytes are read in full regardless of whether the stream could be seeked. This is
        // the one place ImageSharp is structurally cheaper -- it identifies in place and rewinds.
        var encoded = EncodedBuffer.Read(stream);

        try
        {
            return load(encoded.Span, options);
        }
        finally
        {
            encoded.Dispose();
        }
    }

    private static unsafe ImageRawData load(ReadOnlySpan<byte> encoded, ImageLoadOptions options)
    {
        int sourceWidth, sourceHeight;

        fixed (byte* source = encoded)
        {
            int info = StbImageNative.sakura_image_info(source, encoded.Length, out sourceWidth, out sourceHeight);

            if (info != StbImageNative.OK)
                throw new InvalidDataException($"stb could not read the image header: {StbImageNative.Describe(info)}.");
        }

        (int srcX, int srcY, int srcWidth, int srcHeight, int width, int height) = plan(sourceWidth, sourceHeight, options);

        var raw = ImageRawData.Rent(width, height);

        try
        {
            var destination = raw.GetWritableSpan();

            fixed (byte* source = encoded)
            fixed (byte* target = destination)
            {
                int result = StbImageNative.sakura_image_load(source, encoded.Length,
                    srcX, srcY, srcWidth, srcHeight,
                    target, width, height, destination.Length);

                if (result != StbImageNative.OK)
                    throw new InvalidDataException($"stb could not decode the image: {StbImageNative.Describe(result)}.");
            }
        }
        catch
        {
            raw.Dispose();
            throw;
        }

        return raw;
    }

    /// <summary>
    /// The source region to keep and the size to scale it to, from <see cref="ImageReduction"/> so this
    /// loader and the ImageSharp one cannot disagree about what a given
    /// <see cref="ImageLoadOptions"/> means.
    /// </summary>
    private static (int SourceX, int SourceY, int SourceWidth, int SourceHeight, int Width, int Height) plan(int sw, int sh, ImageLoadOptions options)
    {
        if (!options.HasTarget)
            return (0, 0, sw, sh, sw, sh);

        (int tw, int th) = ImageReduction.TargetPixels(options.TargetSize!.Value);

        if (options.CropToFill)
        {
            var fill = ImageReduction.FillRegion(sw, sh, tw, th);
            return (fill.SourceX, fill.SourceY, fill.SourceWidth, fill.SourceHeight, fill.Width, fill.Height);
        }

        (int width, int height) = ImageReduction.FitSize(sw, sh, tw, th);
        return (0, 0, sw, sh, width, height);
    }

    /// <summary>
    /// An encoded image's bytes in one contiguous, pooled buffer. stb needs the whole file at once
    /// </summary>
    private readonly struct EncodedBuffer : IDisposable
    {
        private readonly byte[] array;
        private readonly int length;

        private EncodedBuffer(byte[] array, int length)
        {
            this.array = array;
            this.length = length;
        }

        public ReadOnlySpan<byte> Span => array.AsSpan(0, length);

        public static EncodedBuffer Read(Stream stream)
        {
            // A seekable stream reports its remaining length, so the buffer is rented once at the right
            // size. Everything else grows, and only then is copied into a rental.
            if (stream.CanSeek)
            {
                int remaining = checked((int)(stream.Length - stream.Position));
                byte[] exact = ArrayPool<byte>.Shared.Rent(remaining);
                stream.ReadExactly(exact.AsSpan(0, remaining));
                return new EncodedBuffer(exact, remaining);
            }

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);

            int grown = (int)buffer.Length;
            byte[] rented = ArrayPool<byte>.Shared.Rent(grown);
            buffer.GetBuffer().AsSpan(0, grown).CopyTo(rented);
            return new EncodedBuffer(rented, grown);
        }

        public void Dispose()
        {
            if (array != null)
                ArrayPool<byte>.Shared.Return(array);
        }
    }
}
