// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// The basic image loader using ImageSharp.
/// </summary>
public class ImageSharpImageLoader : IImageLoader
{
    public ImageRawData Load(Stream stream)
    {
        using var image = Image.Load<Rgba32>(stream);
        {
            image.Mutate(x => x.AutoOrient());

            byte[] pixels = new byte[image.Width * image.Height * 4];
            image.CopyPixelDataTo(pixels);

            return new ImageRawData(image.Width, image.Height, pixels);
        }
    }
}
