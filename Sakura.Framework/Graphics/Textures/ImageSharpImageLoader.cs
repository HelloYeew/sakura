// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
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
    public ImageRawData Load(Stream stream) => Load(stream, 0);

    public ImageRawData Load(Stream stream, int maxDimension)
    {
        if (maxDimension > 0)
        {
            byte[] bytes = readAll(stream);
            return load(bytes, decodeSizeFor(bytes, maxDimension), maxDimension);
        }

        using var image = Image.Load<Rgba32>(stream);
        return finish(image);
    }

    private static ImageRawData load(byte[] bytes, Size? decodeTarget, int maxDimension)
    {
        var options = new DecoderOptions { TargetSize = decodeTarget };
        using var image = Image.Load<Rgba32>(options, new MemoryStream(bytes));

        if (Math.Max(image.Width, image.Height) > maxDimension)
        {
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(maxDimension, maxDimension),
                Mode = ResizeMode.Max
            }));
        }

        return finish(image);
    }

    private static ImageRawData finish(Image<Rgba32> image)
    {
        image.Mutate(x => x.AutoOrient());

        byte[] pixels = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(pixels);

        return new ImageRawData(image.Width, image.Height, pixels);
    }

    /// <summary>
    /// The size hint passed to the decoder: the source scaled so its longest edge is
    /// <paramref name="maxDimension"/>, or <c>null</c> when the source is already small enough (so it is
    /// never upscaled) or its header can't be read.
    /// </summary>
    private static Size? decodeSizeFor(byte[] bytes, int maxDimension)
    {
        int sw, sh;
        try
        {
            var info = Image.Identify(bytes);
            sw = info.Width;
            sh = info.Height;
        }
        catch
        {
            return null;
        }

        int longest = Math.Max(sw, sh);
        if (longest <= 0 || longest <= maxDimension)
            return null;

        float scale = (float)maxDimension / longest;
        return new Size(
            Math.Max(1, (int)MathF.Ceiling(sw * scale)),
            Math.Max(1, (int)MathF.Ceiling(sh * scale))
        );
    }

    private static byte[] readAll(Stream stream)
    {
        if (stream is MemoryStream existing)
            return existing.ToArray();

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
