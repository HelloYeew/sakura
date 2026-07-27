// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Buffers;
using System.IO;
using Sakura.Framework.Maths;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// The basic image loader using ImageSharp.
/// </summary>
public class ImageSharpImageLoader : IImageLoader
{
    public ImageRawData Load(Stream stream) => Load(stream, ImageLoadOptions.FullSize);

    public ImageRawData Load(Stream stream, int maxDimension) => Load(stream, ImageLoadOptions.MaxDimension(maxDimension));

    public ImageRawData Load(Stream stream, ImageLoadOptions options)
    {
        if (!options.HasTarget)
        {
            using var full = Image.Load<Rgba32>(stream);
            return finish(full);
        }

        var target = options.TargetSize!.Value;
        bool crop = options.CropToFill;

        var encoded = EncodedBuffer.Read(stream);

        try
        {
            var decoderOptions = new DecoderOptions { TargetSize = decodeSizeFor(encoded.Span, target, crop) };

            using var encodedStream = encoded.AsStream();
            using var image = Image.Load<Rgba32>(decoderOptions, encodedStream);

            return finish(image, target, crop);
        }
        finally
        {
            encoded.Dispose();
        }
    }

    private static ImageRawData finish(Image<Rgba32> image) => finish(image, null, false);

    private static ImageRawData finish(Image<Rgba32> image, Vector2? target, bool cropToFill)
    {
        image.Mutate(x => x.AutoOrient());

        if (target is { } size)
            reduce(image, size, cropToFill);

        byte[] pixels = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(pixels);

        return new ImageRawData(image.Width, image.Height, pixels);
    }

    /// <summary>
    /// The size hint passed to the decoder, or <c>null</c> to decode at full resolution. Only ever
    /// downscales (a source already at or below the target is never enlarged), and keeps enough
    /// resolution for the region that will ultimately be displayed so <see cref="reduce"/> is a clean
    /// final shrink
    /// </summary>
    private static Size? decodeSizeFor(ReadOnlySpan<byte> encoded, Vector2 target, bool cropToFill)
    {
        int tw = Math.Max(1, (int)MathF.Ceiling(target.X));
        int th = Math.Max(1, (int)MathF.Ceiling(target.Y));

        int sw, sh;

        try
        {
            var info = Image.Identify(encoded);
            sw = info.Width;
            sh = info.Height;
        }
        catch
        {
            return null; // header unreadable, fall back to a full decode, reduce() still caps it
        }

        if (sw <= 0 || sh <= 0)
            return null;

        float targetAspect = (float)tw / th;
        float srcAspect = (float)sw / sh;

        // for a Fill crop one dimension is kept whole, so only the other needs to reach the target
        // for a fit the whole image must be contained in the box
        float scale = cropToFill
            ? (srcAspect < targetAspect ? (float)tw / sw : (float)th / sh)
            : MathF.Min((float)tw / sw, (float)th / sh);

        if (scale >= 1f)
            return null;

        return new Size(
            Math.Max(1, (int)MathF.Ceiling(sw * scale)),
            Math.Max(1, (int)MathF.Ceiling(sh * scale))
        );
    }

    private static void reduce(Image<Rgba32> image, Vector2 target, bool cropToFill)
    {
        int tw = Math.Max(1, (int)MathF.Ceiling(target.X));
        int th = Math.Max(1, (int)MathF.Ceiling(target.Y));

        if (cropToFill)
        {
            // keep only the center region a Fill would actually display, the rest is clipped off
            // screen, so carrying it wastes decode time, memory and upload bandwidth.
            float targetAspect = (float)tw / th;
            float srcAspect = (float)image.Width / image.Height;

            int cropW, cropH;

            if (srcAspect > targetAspect)
            {
                cropH = image.Height;
                cropW = Math.Max(1, (int)MathF.Round(cropH * targetAspect));
            }
            else
            {
                cropW = image.Width;
                cropH = Math.Max(1, (int)MathF.Round(cropW / targetAspect));
            }

            if (cropW < image.Width || cropH < image.Height)
            {
                int x = (image.Width - cropW) / 2;
                int y = (image.Height - cropH) / 2;
                image.Mutate(i => i.Crop(new Rectangle(x, y, cropW, cropH)));
            }
        }

        // only ever downscale, upscaling a small source just wastes memory.
        if (image.Width > tw || image.Height > th)
        {
            image.Mutate(i => i.Resize(new ResizeOptions
            {
                Size = new Size(tw, th),
                // after a crop the aspect already matches, so Max is exact
                // without one it fits the image inside the box preserving aspect
                Mode = ResizeMode.Max
            }));
        }
    }

    /// <summary>
    /// A right-sized (and, where possible, pooled) copy of an encoded image's bytes, readable both as a
    /// span (for <see cref="Image.Identify(ReadOnlySpan{byte})"/>) and as a non-copying stream (for the
    /// decode itself).
    /// </summary>
    private readonly struct EncodedBuffer : IDisposable
    {
        private readonly byte[] array;
        private readonly int length;
        private readonly bool pooled;

        private EncodedBuffer(byte[] array, int length, bool pooled)
        {
            this.array = array;
            this.length = length;
            this.pooled = pooled;
        }

        public ReadOnlySpan<byte> Span => array.AsSpan(0, length);

        /// <summary>
        /// A read-only stream over the buffered bytes. Wraps the existing array rather than copying it.
        /// </summary>
        public Stream AsStream() => new MemoryStream(array, 0, length, writable: false);

        public static EncodedBuffer Read(Stream stream)
        {
            if (stream.CanSeek)
            {
                long remaining = stream.Length - stream.Position;

                if (remaining > 0 && remaining <= int.MaxValue)
                {
                    byte[] rented = ArrayPool<byte>.Shared.Rent((int)remaining);
                    int read = 0;

                    while (read < remaining)
                    {
                        int count = stream.Read(rented, read, (int)remaining - read);
                        if (count <= 0)
                            break;

                        read += count;
                    }

                    return new EncodedBuffer(rented, read, pooled: true);
                }
            }

            // non-seekable or unknown length: grow, then hand over the stream's own buffer (no copy)
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return new EncodedBuffer(ms.GetBuffer(), (int)ms.Length, pooled: false);
        }

        public void Dispose()
        {
            if (pooled)
                ArrayPool<byte>.Shared.Return(array);
        }
    }
}
