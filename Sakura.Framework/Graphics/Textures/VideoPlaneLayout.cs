// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// How a decoded video frame's samples are spread across the textures a backend binds for it. The
/// frame reaches the GPU in whichever of these the decoder produced, so the layout picks both the
/// plane set a <see cref="INativeVideoTexture"/> allocates and the shader variant that samples it.
/// </summary>
public enum VideoPlaneLayout
{
    /// <summary>
    /// Three single-channel planes: full-resolution Y, then half-resolution U and V. What software
    /// decode produces directly, and what <c>sws_scale</c> converts anything unrecognised into.
    /// </summary>
    Yuv420P,

    /// <summary>
    /// Two planes: full-resolution single-channel Y, and a half-resolution two-channel plane holding
    /// Cb and Cr interleaved. What every hardware decoder hands back, so sampling it directly is what
    /// removes the conversion rather than paying for one.
    /// </summary>
    Nv12,
}

public static class VideoPlaneLayoutExtensions
{
    /// <summary>
    /// How many textures the layout occupies — and so how many consecutive slots
    /// <see cref="INativeVideoTexture.BindPlanes"/> fills, starting at 0.
    /// </summary>
    public static int PlaneCount(this VideoPlaneLayout layout) => layout == VideoPlaneLayout.Nv12 ? 2 : 3;
}
