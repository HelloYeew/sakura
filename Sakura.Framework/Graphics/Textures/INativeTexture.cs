// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Text;

namespace Sakura.Framework.Graphics.Textures;

public interface INativeTexture : IDisposable
{
    /// <summary>
    /// Backend-specific GPU handle.
    /// For OpenGL this is the uint texture name cast to nint.
    /// For Metal this is the MTLTexture* pointer.
    /// </summary>
    nint Handle { get; }

    int Width { get; }
    int Height { get; }

    /// <summary>
    /// True once the texture has been uploaded to the GPU and is safe to render.
    /// </summary>
    bool Available { get; }

    /// <summary>
    /// Uploads the full texture data. Must be called on the draw/render thread.
    /// Data is expected in RGBA8 format, row-major.
    /// </summary>
    void Upload(ReadOnlySpan<byte> data);

    /// <summary>
    /// Uploads a sub-region of the texture. Must be called on the draw/render thread.
    /// Used by <see cref="TextureAtlas"/> to blit individual glyphs or sprites into an atlas page.
    /// Data is expected in RGBA8 format, row-major, covering the given region dimensions.
    /// </summary>
    void UploadRegion(int x, int y, int width, int height, ReadOnlySpan<byte> data);

    /// <summary>
    /// Binds this texture to the specified texture slot for the next draw call.
    /// For OpenGL: activates TextureUnit.Texture0 + slot and calls BindTexture.
    /// For Metal: records the slot for the current render command encoder.
    /// </summary>
    void Bind(int slot = 0);
}
