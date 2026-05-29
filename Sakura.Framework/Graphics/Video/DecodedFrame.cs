// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Textures;

namespace Sakura.Framework.Graphics.Video;

/// <summary>
/// A decoded video frame ready for display.
/// <para>
/// <see cref="Texture"/> is a dimension-only wrapper used by <see cref="Sakura.Framework.Graphics.Drawables.Drawable"/>
/// for FillMode layout calculations. It carries no GL handles.
/// </para>
/// <para>
/// <see cref="NativeTexture"/> is the actual GPU resource (YUV planes + upload lifecycle).
/// <see cref="Sakura.Framework.Graphics.Video.VideoSprite"/> reads this directly — it never
/// touches <see cref="Texture"/> for rendering.
/// </para>
/// The texture pool is owned by <see cref="VideoDecoder"/>. Return frames via
/// <see cref="VideoDecoder.ReturnFrames"/> when done.
/// </summary>
public class DecodedFrame
{
    /// <summary>
    /// Presentation timestamp in milliseconds.
    /// </summary>
    public double Time { get; init; }

    /// <summary>
    /// Dimension-only texture for layout (FillMode, Width, Height).
    /// Contains no GL handles — safe to use from any thread.
    /// </summary>
    public Texture Texture { get; init; } = null!;

    /// <summary>
    /// The actual GPU resource for this frame.
    /// Owned by <see cref="VideoDecoder"/>'s texture pool.
    /// </summary>
    public VideoTexture NativeTexture { get; init; } = null!;
}
