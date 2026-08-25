// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using Sakura.Framework.Logging;

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

    /// <summary>
    /// Loads image data from the provided stream, reduced according to <paramref name="options"/>.
    /// </summary>
    /// <remarks>
    /// Prefer this over <see cref="Load(Stream, int)"/> when the image will be drawn with
    /// <see cref="TextureFillMode.Fill"/>, a Fill only shows the center band of the source, so capping
    /// the longest edge alone still keeps (and uploads) pixels that are clipped off-screen. A 1920×1080
    /// background bound for a 768×128 bar is 768×432 under a longest-edge cap but only 768×128 when the
    /// band is cropped — 3.4× fewer pixels for the same result on screen.
    /// </remarks>
    /// <param name="stream">The input stream containing image data.</param>
    /// <param name="options">How to reduce the image while decoding.</param>
    /// <returns>The raw image data.</returns>
    ImageRawData Load(Stream stream, ImageLoadOptions options)
        // Default implementation so existing implementors keep compiling: honours the target size but
        // not the Fill crop. Override to support cropping.
        => Load(stream, options.TargetSize is { } size ? (int)MathF.Ceiling(MathF.Max(size.X, size.Y)) : 0);

    /// <summary>
    /// Logs which decoder this is and what it can do, once at startup.
    /// </summary>
    void LogInfo() => Logger.Verbose($"🖼️ {GetType().Name} initialized");
}
