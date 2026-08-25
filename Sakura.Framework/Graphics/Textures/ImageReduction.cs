// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// The sizing arithmetic shared by every <see cref="IImageLoader"/>
/// </summary>
internal static class ImageReduction
{
    /// <summary>
    /// The target rounded out to whole pixels, at least 1 on each axis.
    /// </summary>
    internal static (int Width, int Height) TargetPixels(Vector2 target)
        => (Math.Max(1, (int)MathF.Ceiling(target.X)), Math.Max(1, (int)MathF.Ceiling(target.Y)));

    /// <summary>
    /// The size a decoder should be asked to produce, or <c>null</c> to decode at full resolution.
    /// Only ever downscales (a source already at or below the target is never enlarged), and keeps
    /// enough resolution for the region that will ultimately be displayed so the final reduction is a
    /// clean single shrink.
    /// </summary>
    internal static (int Width, int Height)? DecodeFraction(int sw, int sh, Vector2 target, bool cropToFill)
    {
        (int tw, int th) = TargetPixels(target);

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

        // the smallest decode that still covers everything the reduction will keep. Going below this
        // would blur the result, since the missing detail cannot be recovered by the final resize.
        int wantedWidth = Math.Max(1, (int)MathF.Ceiling(sw * scale));
        int wantedHeight = Math.Max(1, (int)MathF.Ceiling(sh * scale));

        // Note : Why power of two
        // measured against a full decode, 1/8, 1/4 and 1/2 all pay for themselves, while 3/8, 5/8, 6/8 and 7/8 are slower
        // than not hinting. Falling out of the loop means no fraction covers the target (the reduction
        // wanted is less than half), so null decodes at full resolution and the reduction does all the work.
        for (int denominator = 8; denominator > 1; denominator /= 2)
        {
            int width = (sw + denominator - 1) / denominator;
            int height = (sh + denominator - 1) / denominator;

            if (width >= wantedWidth && height >= wantedHeight)
                return (width, height);
        }

        return null;
    }

    /// <summary>
    /// Calculate the exact output size for a Fill by the center region of a
    /// <paramref name="sw"/> x <paramref name="sh"/> source that a
    /// <paramref name="tw"/> x <paramref name="th"/> box would display, scaled down to that box if it is
    /// larger. Never larger than the region itself, so a resize mode that would otherwise happily
    /// enlarge a small source only ever downscales.
    /// </summary>
    /// <remarks>
    /// The center region itself is not returned, only the size it resolves to, because ImageSharp's
    /// ResizeMode.Crop selects the region internally. A decoder that has to crop for itself
    /// (stb_image_resize2 takes a sub-rectangle) needs the origin as well, which follows from the
    /// aspect comparison below. But it is not exposed until something calls it, so that it is not
    /// carrying rounding behavior no test pins.
    /// </remarks>
    internal static (int Width, int Height) FillSize(int sw, int sh, int tw, int th)
    {
        float targetAspect = (float)tw / th;
        float srcAspect = (float)sw / sh;

        // the region a Fill actually displays: one axis kept whole, the other cut to the target aspect.
        // The rest is clipped off-screen, so carrying it wastes decode time, memory and upload bandwidth.
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

        return (
            Math.Max(1, (int)MathF.Round(cropW * scale)),
            Math.Max(1, (int)MathF.Round(cropH * scale))
        );
    }
}
