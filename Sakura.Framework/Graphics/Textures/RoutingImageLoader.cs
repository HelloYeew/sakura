// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using Sakura.Framework.Graphics.Textures.ImageSharp;
using Sakura.Framework.Graphics.Textures.Stb;
using Sakura.Framework.Logging;
using Sakura.Framework.Statistic;
using SixLabors.ImageSharp;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// Image loader that decodes with <see cref="StbImageLoader"/>
/// and falls back to <see cref="ImageSharpImageLoader"/>
/// </summary>
/// <remarks>
/// Stb should be the default loader unless got some not-familiar format that stb is not support
/// (e.g. WebP, TIFF, QOI, PBM, etc.). ImageSharp will got initialize and use later.
/// </remarks>
public class RoutingImageLoader : IImageLoader
{
    /// <summary>
    /// Images that went to ImageSharp because stb could not decode them.
    /// </summary>
    /// <remarks>
    /// Reads zero on a library of JPEGs and PNGs. A number that climbs means the fallback is carrying
    /// real traffic, which is worth knowing before concluding stb is doing the work.
    /// </remarks>
    private static readonly GlobalStatistic<long> stat_fallbacks = GlobalStatistics.Get<long>("Textures", "ImageSharp Fallbacks", StatisticKind.Cumulative);

    /// <summary>
    /// Images routed to ImageSharp up front because they declare an EXIF orientation.
    /// </summary>
    private static readonly GlobalStatistic<long> stat_oriented = GlobalStatistics.Get<long>("Textures", "ImageSharp Oriented Routes", StatisticKind.Cumulative);

    /// <summary>
    /// JPEGs routed to ImageSharp up front to be decoded already reduced.
    /// </summary>
    private static readonly GlobalStatistic<long> stat_scaled = GlobalStatistics.Get<long>("Textures", "ImageSharp Scaled JPEG Routes", StatisticKind.Cumulative);

    private readonly StbImageLoader stb = new StbImageLoader();

    private readonly Lazy<ImageSharpImageLoader> imageSharp =
        new Lazy<ImageSharpImageLoader>(() => new ImageSharpImageLoader(), isThreadSafe: true);

    /// <summary>
    /// The loader that will handle an image, without decoding it. For diagnostics and tests.
    /// </summary>
    public bool WouldUseStb(ReadOnlySpan<byte> encoded)
        => WouldUseStb(encoded, ImageLoadOptions.FullSize);

    /// <summary>
    /// The loader that will handle an image wanted at <paramref name="options"/>, without decoding it. For diagnostics
    /// and tests.
    /// </summary>
    public bool WouldUseStb(ReadOnlySpan<byte> encoded, ImageLoadOptions options)
        => StbImageLoader.IsAvailable && !ExifOrientation.RequiresTransform(encoded) && !decodesReduced(encoded, options);

    /// <summary>
    /// How many pixels a JPEG has to have before it's decoded reduced rather than whole: ImageSharp decodes a JPEG wanted
    /// at half its size or less at that size, by scaling its DCT, where stb decodes the whole image first. Measured on an
    /// M3 for a 1920x1080 screen: 24 MP took 320 ms either way but stb peaked ~150 MB higher; 12 MP was 22% slower
    /// through ImageSharp for 48 MB less; at 2-4 MP the two were within a few milliseconds. So a desktop, with memory to
    /// spare, sends only the largest photos, and a phone, where a spike that size gets an app ended, sends more.
    /// </summary>
    private static readonly long scaled_decode_min_pixels = RuntimeInfo.IsMobile ? 4_000_000 : 16_000_000;

    /// <summary>
    /// Whether this is a JPEG large enough, and wanted small enough, to be decoded reduced (see
    /// <see cref="scaled_decode_min_pixels"/>).
    /// </summary>
    private static bool decodesReduced(ReadOnlySpan<byte> encoded, ImageLoadOptions options)
    {
        // JPEG's start-of-image marker.
        if (!options.HasTarget || encoded.Length < 3 || encoded[0] != 0xFF || encoded[1] != 0xD8 || encoded[2] != 0xFF)
            return false;

        try
        {
            var info = Image.Identify(encoded);

            return (long)info.Width * info.Height >= scaled_decode_min_pixels
                   && ImageReduction.DecodeFraction(info.Width, info.Height, options.TargetSize!.Value, options.CropToFill) != null;
        }
        catch
        {
            // A header ImageSharp can't read: stb has its turn, and the fallback after it.
            return false;
        }
    }

    public void LogInfo()
    {
        Logger.Verbose("🖼️ Routing image loader initialized");
        Logger.Verbose($"Routing Primary: {nameof(StbImageLoader)}{(StbImageLoader.IsAvailable ? "" : " (unavailable — everything falls back)")}");
        Logger.Verbose($"Routing Fallback: {nameof(ImageSharpImageLoader)} (loaded on first use)");
        Logger.Verbose($"Routing Rule: EXIF-oriented images, and JPEGs of {scaled_decode_min_pixels / 1_000_000} MP or more wanted at half size or less, go to the fallback up front; anything stb rejects falls back on error");

        stb.LogInfo();

        if (imageSharp.IsValueCreated)
            imageSharp.Value.LogInfo();
    }

    public ImageRawData Load(Stream stream) => Load(stream, ImageLoadOptions.FullSize);

    public ImageRawData Load(Stream stream, int maxDimension) => Load(stream, ImageLoadOptions.MaxDimension(maxDimension));

    public ImageRawData Load(Stream stream, ImageLoadOptions options)
    {
        // Read once and share. stb needs the whole file regardless, and the orientation check needs the
        // header before either decoder is chosen, so buffering here costs nothing the stb path was not
        // already paying.
        using var encoded = EncodedImage.Read(stream);

        if (!StbImageLoader.IsAvailable)
            return imageSharp.Value.Load(encoded.AsStream(), options);

        if (ExifOrientation.RequiresTransform(encoded.Span))
        {
            stat_oriented.Value++;
            return imageSharp.Value.Load(encoded.AsStream(), options);
        }

        if (decodesReduced(encoded.Span, options))
        {
            stat_scaled.Value++;
            return imageSharp.Value.Load(encoded.AsStream(), options);
        }

        try
        {
            return StbImageLoader.Load(encoded.Span, options);
        }
        catch (Exception e)
        {
            // Deliberately broad. The point of a fallback is that the second decoder gets a turn
            // whatever the first one objected to, and the alternative to catching an unanticipated
            // exception here is failing a texture load that ImageSharp could have served.
            Logger.Verbose($"stb could not decode this image ({e.GetType().Name}: {e.Message}); falling back to ImageSharp.");
            stat_fallbacks.Value++;

            return imageSharp.Value.Load(encoded.AsStream(), options);
        }
    }
}
