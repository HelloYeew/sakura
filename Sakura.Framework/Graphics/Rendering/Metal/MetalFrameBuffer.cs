// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Textures;

namespace Sakura.Framework.Graphics.Rendering.Metal;

/// <summary>
/// The Metal implementation of <see cref="IFrameBuffer"/>
/// </summary>
public sealed class MetalFrameBuffer : IFrameBuffer
{
    private readonly nint device; // SakuraMetalDevice*

    private MetalTexture colorTexture;

    public Texture Texture { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>
    /// The native render-target texture handle (MTLTexture*), used by the renderer to begin an
    /// offscreen pass into this buffer.
    /// </summary>
    internal nint TextureHandle => colorTexture.Handle;

    public MetalFrameBuffer(nint device, int width, int height)
    {
        this.device = device;
        createAttachment(width, height);
    }

    private void createAttachment(int width, int height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);

        colorTexture?.Dispose();
        colorTexture = MetalTexture.CreateRenderTarget(device, Width, Height);
        Texture = new Texture(colorTexture);
    }

    public void Resize(int width, int height)
    {
        if (width == Width && height == Height)
            return;

        createAttachment(width, height);
    }

    public void Dispose()
    {
        colorTexture?.Dispose();
        colorTexture = null;
        Texture = null;
    }
}
