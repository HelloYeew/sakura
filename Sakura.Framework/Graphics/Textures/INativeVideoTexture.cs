// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using FFmpeg.AutoGen;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// Backend-agnostic contract for a video texture. The plane set is fixed at construction by
/// <see cref="Layout"/>; a frame in a different layout needs a different texture, not a re-upload.
/// The GL implementation is <c>VideoGLTexture</c>; Metal and Direct3D 11 have their own.
/// </summary>
public interface INativeVideoTexture : IDisposable
{
    int Width { get; }
    int Height { get; }

    /// <summary>
    /// Which plane set this texture holds, and so which shader variant can sample it. Fixed at
    /// construction.
    /// </summary>
    VideoPlaneLayout Layout { get; }

    /// <summary>
    /// True once at least one frame has been uploaded and is ready to draw.
    /// </summary>
    bool Available { get; }

    /// <summary>
    /// How many bytes of GPU memory this texture's planes occupy, or <c>0</c> when it borrows them
    /// rather than owning them.
    /// </summary>
    /// <remarks>
    /// Asked of the texture rather than derived from its dimensions, because dimensions no longer
    /// determine the answer: a zero-copy texture is a view onto a frame the decoder owns and costs
    /// nothing, while a copying one of the same size costs 1.5 bytes per pixel. This is the figure the
    /// <c>video</c> line in <see cref="NativeMemoryTracker"/> is built from, so anything displaying a
    /// size should use it and stay in agreement.
    /// </remarks>
    long PlaneBytes { get; }

    /// <summary>
    /// How often this texture has been bound, per frame. One <see cref="BindPlanes"/> counts once, not
    /// once per plane: the planes are always bound together, so counting each would only ever report
    /// the same number multiplied by the plane count. See <see cref="TextureBindTracker"/>.
    /// </summary>
    TextureBindCounter Binds { get; }

    /// <summary>
    /// Binds this texture's planes to consecutive slots from 0, in <see cref="Layout"/> order —
    /// Y, U, V for <see cref="VideoPlaneLayout.Yuv420P"/>, Y then interleaved CbCr for
    /// <see cref="VideoPlaneLayout.Nv12"/>.
    /// For OpenGL: activates TextureUnit.Texture0/1(/2) and binds each plane.
    /// For Metal: records the plane textures on the current render command encoder.
    /// Must be called on the render thread.
    /// </summary>
    /// <param name="tiling">
    /// When true (the sprite uses <c>TextureFillMode.Tile</c>), the planes are sampled with a
    /// repeating wrap so UVs &gt; 1 tile the frame; otherwise they clamp to edge (the normal video
    /// case, which avoids edge bleed). Wrap is a per-bind state, not baked into the plane textures.
    /// </param>
    void BindPlanes(bool tiling);

    /// <summary>
    /// Uploads a decoded frame into the backend texture planes. The frame must be in this texture's
    /// <see cref="Layout"/>; the decoder guarantees that by retiring the pool whenever the format
    /// changes. Must be called on the render thread.
    /// </summary>
    unsafe void Upload(AVFrame* frame);

    /// <summary>
    /// Marks the texture as having valid uploaded data.
    /// Called by the upload path after all planes are transferred to the GPU.
    /// </summary>
    void MarkAvailable();
}
