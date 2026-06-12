// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Textures;

namespace Sakura.Framework.Graphics.Rendering;

/// <summary>
/// An offscreen render target: draw commands issued between
/// <see cref="IRenderer.BindFrameBuffer"/> and <see cref="IRenderer.UnbindFrameBuffer"/>
/// render into <see cref="Texture"/> instead of the screen.
/// <remarks>
/// All members must be used on the draw thread only.
/// </remarks>
/// </summary>
public interface IFrameBuffer : IDisposable
{
    /// <summary>
    /// The color texture containing whatever was last rendered into this framebuffer.
    /// The instance may change after <see cref="Resize"/>; re-read it per frame.
    /// </summary>
    Texture Texture { get; }

    /// <summary>
    /// Current width of the render target in physical pixels.
    /// </summary>
    int Width { get; }

    /// <summary>
    /// Current height of the render target in physical pixels.
    /// </summary>
    int Height { get; }

    /// <summary>
    /// Resizes the render target, discarding its current contents. No-op when the size is unchanged.
    /// </summary>
    void Resize(int width, int height);
}
