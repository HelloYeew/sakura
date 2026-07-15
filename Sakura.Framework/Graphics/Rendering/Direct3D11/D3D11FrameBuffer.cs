// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Textures;
using Vortice.Direct3D11;

namespace Sakura.Framework.Graphics.Rendering.Direct3D11;

/// <summary>
/// The Direct3D 11 implementation of <see cref="IFrameBuffer"/>
/// </summary>
internal sealed class D3D11FrameBuffer : IFrameBuffer
{
    private readonly ID3D11Device device;
    private readonly ID3D11DeviceContext context;

    private D3D11Texture colorTexture;

    public Texture Texture { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>
    /// The render-target view the renderer binds to draw into this buffer
    /// </summary>
    internal ID3D11RenderTargetView RenderTargetView => colorTexture.RenderTargetView;

    public D3D11FrameBuffer(ID3D11Device device, ID3D11DeviceContext context, int width, int height)
    {
        this.device = device;
        this.context = context;
        createAttachment(width, height);
    }

    private void createAttachment(int width, int height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);

        colorTexture?.Dispose();
        colorTexture = D3D11Texture.CreateRenderTarget(device, context, Width, Height);
        Texture = new Texture(colorTexture);
    }

    public void Resize(int width, int height)
    {
        if (Math.Max(1, width) == Width && Math.Max(1, height) == Height)
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
