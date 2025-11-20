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
}
