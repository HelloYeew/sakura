// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Textures;

namespace Sakura.Framework.Graphics.Video;

/// <summary>
/// A decoded video frame ready for display. Holds a GPU texture (which wraps the native YUV data)
/// and the presentation timestamp in milliseconds.
/// The texture is owned by the decoder's texture pool and then return it via <see cref="VideoDecoder.ReturnFrames"/> when done.
/// </summary>
public class DecodedFrame
{
    /// <summary>
    /// Presentation timestamp in milliseconds.
    /// </summary>
    public double Time { get; init; }

    /// <summary>
    /// The YUV texture ready to be drawn by the video shader.
    /// </summary>
    public Texture Texture { get; init; } = null!;
}
