// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.IO;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// Interface for image decoders/loaders.
/// </summary>
public interface IImageLoader
{
    /// <summary>
    /// Loads image data from the provided stream.
    /// </summary>
    /// <param name="stream">The input stream containing image data.</param>
    /// <returns>>The raw image data.</returns>
    ImageRawData Load(Stream stream);

    /// <summary>
    /// Loads image data from the provided stream, decoding it at (at most) <paramref name="maxDimension"/>
    /// pixels on its longest edge, preserving aspect ratio. Decoders that support it (e.g. JPEG) reduce
    /// the image while decoding, so a large source never has to be fully decoded then shrunk. The image is
    /// only ever downscaled, never enlarged.
    /// </summary>
    /// <remarks>
    /// Use this for thumbnails, cover art and other cases where the display size is far smaller than the
    /// source — it avoids decoding, allocating and uploading far more pixels than can be shown.
    /// </remarks>
    /// <param name="stream">The input stream containing image data.</param>
    /// <param name="maxDimension">
    /// The maximum length (px) of the longest edge. Values &lt;= 0 mean "no limit" and behave exactly like
    /// <see cref="Load(Stream)"/>.
    /// </param>
    /// <returns>The raw image data.</returns>
    ImageRawData Load(Stream stream, int maxDimension);
}
