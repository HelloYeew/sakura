// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
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

        // A seekable source can be read twice, so read the header, rewind, and decode straight from it.
        if (stream.CanSeek)
        {
            long origin = stream.Position;
            var decodeSize = decodeSizeFor(stream, target, crop);
            stream.Position = origin;

            using var image = Image.Load<Rgba32>(new DecoderOptions { TargetSize = decodeSize }, stream);
            return finish(image, target, crop);
        }

        // Non-seekable (an embedded resource or an archive entry): the header read cannot be undone, so
        // the bytes have to be buffered before the size hint can be computed.
        var encoded = EncodedBuffer.Read(stream);
        var buffered = new DecoderOptions { TargetSize = decodeSizeFor(encoded.Span, target, crop) };

        using var encodedStream = encoded.AsStream();

        {
            using var image = Image.Load<Rgba32>(buffered, encodedStream);
            return finish(image, target, crop);
        }
    }

    private static ImageRawData finish(Image<Rgba32> image) => finish(image, null, false);

    private static ImageRawData finish(Image<Rgba32> image, Vector2? target, bool cropToFill)
    {
        image.Mutate(x => x.AutoOrient());

        if (target is { } size)
            reduce(image, size, cropToFill);

        // Rented rather than allocated: a full-screen image is tens of megabytes, i.e. a large-object-heap
        // block per decode, and a behavior like a game changing backgrounds does this repeatedly. CopyPixelDataTo fills
        // every byte, so the pool handing back a dirty array is fine.
        var raw = ImageRawData.Rent(image.Width, image.Height);

        try
        {
            image.CopyPixelDataTo(raw.GetWritableSpan());
        }
        catch
        {
            raw.Dispose();
            throw;
        }

        return raw;
    }

    /// <summary>
    /// <see cref="decodeSizeFor(int,int,Vector2,bool)"/> for an already-buffered image.
    /// </summary>
    private static Size? decodeSizeFor(ReadOnlySpan<byte> encoded, Vector2 target, bool cropToFill)
    {
        try
        {
            var info = Image.Identify(encoded);
            return decodeSizeFor(info.Width, info.Height, target, cropToFill);
        }
        catch
        {
            return null; // header unreadable, fall back to a full decode, reduce() still caps it
        }
    }

    /// <summary>
    /// <see cref="decodeSizeFor(int,int,Vector2,bool)"/> for a stream, read in place. Leaves the stream
    /// positioned wherever the header read ended, so the caller must rewind before decoding.
    /// </summary>
    private static Size? decodeSizeFor(Stream stream, Vector2 target, bool cropToFill)
    {
        try
        {
            var info = Image.Identify(stream);
            return decodeSizeFor(info.Width, info.Height, target, cropToFill);
        }
        catch
        {
            return null; // header unreadable, fall back to a full decode, reduce() still caps it
        }
    }

    /// <summary>
    /// The size hint passed to the decoder, or <c>null</c> to decode at full resolution. Only ever
    /// downscales (a source already at or below the target is never enlarged), and keeps enough
    /// resolution for the region that will ultimately be displayed so <see cref="reduce"/> is a clean
    /// final shrink
    /// </summary>
    private static Size? decodeSizeFor(int sw, int sh, Vector2 target, bool cropToFill)
    {
        int tw = Math.Max(1, (int)MathF.Ceiling(target.X));
        int th = Math.Max(1, (int)MathF.Ceiling(target.Y));

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
    /// An encoded image's bytes held in memory, readable both as a span (for
    /// <see cref="Image.Identify(ReadOnlySpan{byte})"/>) and as a non-copying stream (for the decode
    /// itself). Only for sources that cannot be read twice, a seekable stream is identified and decoded
    /// in place instead.
    /// </summary>
    private readonly struct EncodedBuffer
    {
        private readonly byte[] array;
        private readonly int length;

        private EncodedBuffer(byte[] array, int length)
        {
            this.array = array;
            this.length = length;
        }

        public ReadOnlySpan<byte> Span => array.AsSpan(0, length);

        /// <summary>
        /// A read-only stream over the buffered bytes. Wraps the existing array rather than copying it.
        /// </summary>
        public Stream AsStream() => new MemoryStream(array, 0, length, writable: false);

        public static EncodedBuffer Read(Stream stream)
        {
            // the length is not known ahead of time, so grow, then hand over the stream's own buffer
            // rather than the ToArray() copy of it.
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return new EncodedBuffer(ms.GetBuffer(), (int)ms.Length);
        }
    }
}
