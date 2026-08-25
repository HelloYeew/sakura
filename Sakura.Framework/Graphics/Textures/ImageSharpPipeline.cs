// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Maths;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// Everything an <see cref="IImageLoader"/> does to a decoded <see cref="Image{TPixel}"/> once it has
/// one from orient it, reduce it to the requested target, and hand back an <see cref="ImageRawData"/>.
/// </summary>
internal static class ImageSharpPipeline
{
    /// <summary>
    /// Orients and reduces <paramref name="image"/>, then copies it into a rented <see cref="ImageRawData"/>.
    /// </summary>
    /// <param name="image">The decoded image.</param>
    /// <param name="target">The size to reduce to, or <c>null</c> to keep the decoded size.</param>
    /// <param name="cropToFill">Whether to crop the center band to the target aspect before scaling.</param>
    internal static ImageRawData Finish(Image<Rgba32> image, Vector2? target = null, bool cropToFill = false)
    {
        if (NeedsOrientation(image.Metadata))
            image.Mutate(x => x.AutoOrient());

        if (target is { } size)
            Reduce(image, size, cropToFill);

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
    /// Scales <paramref name="image"/> down to <paramref name="target"/> in a single pass, cropping the
    /// centre band to the target aspect first when <paramref name="cropToFill"/> is set. Only ever
    /// downscales.
    /// </summary>
    internal static void Reduce(Image<Rgba32> image, Vector2 target, bool cropToFill)
    {
        (int tw, int th) = ImageReduction.TargetPixels(target);

        if (cropToFill)
        {
            (int width, int height) = ImageReduction.FillSize(image.Width, image.Height, tw, th);

            if (width < image.Width || height < image.Height)
            {
                image.Mutate(i => i.Resize(new ResizeOptions
                {
                    Size = new Size(width, height),
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
    /// <see cref="ImageReduction.DecodeFraction"/> gated on the format being able to act on the hint at
    /// all, as a <see cref="Size"/> for DecoderOptions.TargetSize.
    /// </summary>
    /// <remarks>
    /// Only JPEG can genuinely decode at a reduced scale. Every other decoder produces the full image
    /// and then ImageSharp resizes it internally to whatever was asked for — so the hint buys nothing
    /// and costs a resample, because <see cref="Reduce"/> still has to take it to the final size
    /// afterwards. The internal pass also uses a box resampler, so hinting a PNG downgrades
    /// the result as well as slowing it.
    /// </remarks>
    internal static Size? DecodeSizeFor(ImageInfo info, Vector2 target, bool cropToFill)
    {
        if (info.Metadata.DecodedImageFormat is not JpegFormat)
            return null;

        var fraction = ImageReduction.DecodeFraction(info.Width, info.Height, target, cropToFill);

        return fraction is null ? null : new Size(fraction.Value.Width, fraction.Value.Height);
    }
}
