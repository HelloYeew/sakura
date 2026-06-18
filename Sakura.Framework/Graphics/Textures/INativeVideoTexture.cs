// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using FFmpeg.AutoGen;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// Backend-agnostic contract for a YUV420P video texture.
/// The GL implementation is <c>VideoGLTexture</c>; Metal will have its own.
/// </summary>
public interface INativeVideoTexture : IDisposable
{
    int Width { get; }
    int Height { get; }

    /// <summary>
    /// True once at least one frame has been uploaded and is ready to draw.
    /// </summary>
    bool Available { get; }

    /// <summary>
    /// Binds the Y, U, V planes to the appropriate texture slots for the current backend.
    /// For OpenGL: activates TextureUnit.Texture0/1/2 and binds each plane.
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
    /// Uploads a decoded YUV420P frame into the backend texture planes.
    /// Must be called on the render thread.
    /// </summary>
    unsafe void Upload(AVFrame* frame);

    /// <summary>
    /// Marks the texture as having valid uploaded data.
    /// Called by the upload path after all planes are transferred to the GPU.
    /// </summary>
    void MarkAvailable();
}
