// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Textures;

namespace Sakura.Framework.Graphics.Video;

/// <summary>
/// One compiled video shader per <see cref="VideoPlaneLayout"/>.
/// </summary>
/// <remarks>
/// Both variants are compiled up front rather than on demand. The layout is not known when a
/// <see cref="VideoSprite"/> loads, it arrives with the first decoded frame, and it can change
/// afterward, since toggling hardware acceleration reopens the codec and flips between NV12 and
/// YUV420P. Compiling lazily would put a shader compiler on the draw thread at the exact moment the
/// picture is meant to keep moving.
///
/// The two differ only in their fetch: one samples three single-channel planes, the other a luma
/// plane and an interleaved chroma plane. A single shader branching on a uniform would cost a dead
/// sampler binding and a branch on every video pixel, which is the more expensive way to save a
/// compiler.
/// </remarks>
public sealed class VideoShaderSet : IDisposable
{
    private readonly IShader yuv420P;
    private readonly IShader nv12;

    private bool disposed;

    private VideoShaderSet(IShader yuv420P, IShader nv12)
    {
        this.yuv420P = yuv420P;
        this.nv12 = nv12;
    }

    /// <summary>
    /// Compiles both variants. Must be called on the draw thread, the GL context owner in
    /// multithreaded mode.
    /// </summary>
    public static VideoShaderSet Create(IRenderer renderer) =>
        new VideoShaderSet(
            renderer.CreateShader(renderer.ShaderStorage, "video.vert", "video.frag"),
            renderer.CreateShader(renderer.ShaderStorage, "video.vert", "video_nv12.frag"));

    /// <summary>
    /// The variant that can sample <paramref name="layout"/>.
    /// </summary>
    public IShader For(VideoPlaneLayout layout) => layout == VideoPlaneLayout.Nv12 ? nv12 : yuv420P;

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        yuv420P.Dispose();
        nv12.Dispose();
    }
}
