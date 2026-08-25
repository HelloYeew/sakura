// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using Sakura.Framework.Maths;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// The basic image loader using ImageSharp.
/// </summary>
public class ImageSharpImageLoader : IImageLoader
{
    /// <summary>
    /// Configures ImageSharp process-wide.
    /// </summary>
    static ImageSharpImageLoader()
    {
        Configuration.Default.MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount);
    }

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
        if (NeedsOrientation(image.Metadata))
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
    /// Whether <paramref name="metadata"/> carries an EXIF orientation that would rotate or flip the
    /// image, i.e. whether <c>AutoOrient</c> would actually do anything.
    /// </summary>
    internal static bool NeedsOrientation(ImageMetadata metadata)
    {
        var exif = metadata.ExifProfile;

        if (exif == null || !exif.TryGetValue(ExifTag.Orientation, out var orientation))
            return false;

        // 1 is TopLeft, i.e. already upright. 0 is not a legal value but does occur in the wild, and
        // treating it as upright matches what AutoOrient does with it.
        return orientation.Value is not (0 or 1);
    }

    /// <summary>
    /// <see cref="decodeSizeFor(int,int,Vector2,bool)"/> for an already-buffered image.
    /// </summary>
    private static Size? decodeSizeFor(ReadOnlySpan<byte> encoded, Vector2 target, bool cropToFill)
    {
        try
        {
            return decodeSizeFor(Image.Identify(encoded), target, cropToFill);
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
            return decodeSizeFor(Image.Identify(stream), target, cropToFill);
        }
        catch
        {
            return null; // header unreadable, fall back to a full decode, reduce() still caps it
        }
    }

    /// <summary>
    /// <see cref="decodeSizeFor(int,int,Vector2,bool)"/> gated on the format being able to act on the
    /// hint at all.
    /// </summary>
    /// <remarks>
    /// Only JPEG can genuinely decode at a reduced scale. Every other decoder produces the full image
    /// and then ImageSharp resizes it internally to whatever was asked for — so the hint buys nothing
    /// and costs a resample, because <see cref="reduce"/> still has to take it to the final size
    /// afterwards. The internal pass also uses a box resampler, so hinting a PNG downgrades
    /// the result as well as slowing it.
    /// </remarks>
    private static Size? decodeSizeFor(ImageInfo info, Vector2 target, bool cropToFill)
        => info.Metadata.DecodedImageFormat is JpegFormat
            ? decodeSizeFor(info.Width, info.Height, target, cropToFill)
            : null;

    /// <summary>
    /// The size hint passed to the decoder, or <c>null</c> to decode at full resolution. Only ever
    /// downscales (a source already at or below the target is never enlarged), and keeps enough
    /// resolution for the region that will ultimately be displayed so <see cref="reduce"/> is a clean
    /// final shrink.
    /// </summary>
    /// <remarks>
    /// Deliberately <em>not</em> the display size. A JPEG decoder can only skip work at the scales its
    /// IDCT supports; ask for anything else, and it decodes at the nearest supported scale and resizes
    /// down to what was asked for, so the loader pays two resamples instead of one. Measured on a
    /// 3840x2160 source, hinting a size the decoder cannot produce natively costs more than passing no
    /// hint at all — 109/114/122 ms at 5/8, 6/8 and 7/8 against 55 ms for a full decode. So the hint is
    /// snapped down to a scale that is free, and <see cref="reduce"/> takes it the rest of the way.
    /// </remarks>
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

        // the smallest decode that still covers everything reduce() will keep. Going below this would
        // blur the result, since the missing detail cannot be recovered by the final resize.
        int wantedWidth = Math.Max(1, (int)MathF.Ceiling(sw * scale));
        int wantedHeight = Math.Max(1, (int)MathF.Ceiling(sh * scale));

        // Note : Why power of two
        // measured against a full decode, 1/8, 1/4 and 1/2 all pay for themselves, while 3/8, 5/8, 6/8 and 7/8 are slower
        // than not hinting. Falling out of the loop means no fraction covers the target (the reduction
        // wanted is less than half), so null decodes at full resolution and reduce() does all the work.
        for (int denominator = 8; denominator > 1; denominator /= 2)
        {
            int width = (sw + denominator - 1) / denominator;
            int height = (sh + denominator - 1) / denominator;

            if (width >= wantedWidth && height >= wantedHeight)
                return new Size(width, height);
        }

        return null;
    }

    private static void reduce(Image<Rgba32> image, Vector2 target, bool cropToFill)
    {
        int tw = Math.Max(1, (int)MathF.Ceiling(target.X));
        int th = Math.Max(1, (int)MathF.Ceiling(target.Y));

        if (cropToFill)
        {
            var size = fillSize(image.Width, image.Height, tw, th);

            if (size.Width < image.Width || size.Height < image.Height)
            {
                image.Mutate(i => i.Resize(new ResizeOptions
                {
                    Size = size,
                    Mode = ResizeMode.Crop,
                    Position = AnchorPositionMode.Center
                }));
            }

            return;
        }

        // only ever downscale, upscaling a small source just wastes memory.
        if (image.Width > tw || image.Height > th)
        {
            image.Mutate(i => i.Resize(new ResizeOptions
            {
                Size = new Size(tw, th),
                Mode = ResizeMode.Max
            }));
        }
    }

    /// <summary>
    /// The exact output size for a Fill: the center region of a
    /// <paramref name="sw"/> x <paramref name="sh"/> source that a
    /// <paramref name="tw"/> x <paramref name="th"/> box would display, scaled down to that box if it is
    /// larger. Never larger than the region itself, so passing this to
    /// <see cref="ResizeMode.Crop"/> which would otherwise happily enlarge a small source only ever
    /// downscales.
    /// </summary>
    private static Size fillSize(int sw, int sh, int tw, int th)
    {
        float targetAspect = (float)tw / th;
        float srcAspect = (float)sw / sh;

        // the region a Fill actually displays: one axis kept whole, the other cut to the target aspect.
        // The rest is clipped off screen, so carrying it wastes decode time, memory and upload bandwidth.
        int cropW, cropH;

        if (srcAspect > targetAspect)
        {
            cropH = sh;
            cropW = Math.Max(1, (int)MathF.Round(cropH * targetAspect));
        }
        else
        {
            cropW = sw;
            cropH = Math.Max(1, (int)MathF.Round(cropW / targetAspect));
        }

        float scale = MathF.Min(1f, MathF.Min((float)tw / cropW, (float)th / cropH));

        return new Size(
            Math.Max(1, (int)MathF.Round(cropW * scale)),
            Math.Max(1, (int)MathF.Round(cropH * scale))
        );
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
