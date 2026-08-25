// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using Sakura.Framework.Logging;

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

    public void LogInfo()
    {
        if (!IsAvailable)
        {
            Logger.Verbose("🖼️ stb image loader initialized, but libsakura-image is not available on this platform");
            return;
        }

        Logger.Verbose("🖼️ stb image loader initialized");
        Logger.Verbose($"stb ABI: {StbImageNative.sakura_image_abi_version()} (expected {StbImageNative.ABI_VERSION})");
        Logger.Verbose($"stb_image Version: {StbImageNative.StbVersion}");
        Logger.Verbose($"stb_image_resize2 Version: {StbImageNative.StbResizeVersion}");
        Logger.Verbose($"stb Formats: {StbImageNative.Formats}");
        // Both are behavioral differences from ImageSharp rather than trivia, and both are invisible in
        // output that happens to look fine, so they are stated every run.
        // TODO: Maybe remove it if not informative???
        Logger.Verbose("stb Resampling: Catmull-Rom, gamma-correct (linear light)");
        Logger.Verbose("stb EXIF Orientation: not supported (stb ignores it)");
        Logger.Verbose("stb Scaled Decode: none (always decodes full resolution)");
    }

    public ImageRawData Load(Stream stream) => Load(stream, ImageLoadOptions.FullSize);

    public ImageRawData Load(Stream stream, int maxDimension) => Load(stream, ImageLoadOptions.MaxDimension(maxDimension));

    public ImageRawData Load(Stream stream, ImageLoadOptions options)
    {
        // stb has no streaming entry point: every decoder in it works from one contiguous buffer, so
        // the encoded bytes are read in full regardless of whether the stream could be seeked. This is
        // the one place ImageSharp is structurally cheaper -- it identifies in place and rewinds.
        var encoded = EncodedImage.Read(stream);

        try
        {
            return Load(encoded.Span, options);
        }
        finally
        {
            encoded.Dispose();
        }
    }

    /// <summary>
    /// Decodes bytes the caller already holds. Internal so a router that has read the header can hand
    /// the same buffer straight over rather than making this read and rent a second copy of it.
    /// </summary>
    internal static unsafe ImageRawData Load(ReadOnlySpan<byte> encoded, ImageLoadOptions options)
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

}
