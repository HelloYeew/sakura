// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Textures;

namespace Sakura.Framework.Graphics.Rendering.Direct3D11;

/// <summary>
/// <see cref="IFrameBuffer"/> for the Direct3D 11 backend.
/// TODO: Replace with real one
/// </summary>
internal sealed class D3D11FrameBuffer : IFrameBuffer
{
    public Texture Texture { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    public D3D11FrameBuffer(int width, int height) => Resize(width, height);

    public void Resize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);

        if (width == Width && height == Height && Texture != null)
            return;

        Width = width;
        Height = height;

        // TODO: no INativeTexture yet
        Texture = new Texture(Width, Height);
    }

    public void Dispose()
    {
    }
}
