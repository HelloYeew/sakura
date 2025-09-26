// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Silk.NET.OpenGL;

namespace Sakura.Framework.Graphics.Textures;

public class Texture : IDisposable
{
    public uint Handle { get; }
    public int Width { get; }
    public int Height { get; }

    private readonly GL _gl;
    private bool _disposed;

    public static Texture WhitePixel;

    public Texture(GL gl, int width, int height, ReadOnlySpan<byte> data)
    {
        _gl = gl;
        Width = width;
        Height = height;

        Handle = _gl.GenTexture();
        Bind();

        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, data);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
    }

    public static void CreateWhitePixel(GL gl)
    {
        if (WhitePixel == null)
        {
            byte[] whitePixelData = { 255, 255, 255, 255 };
            WhitePixel = new Texture(gl, 1, 1, whitePixelData);
        }
    }

    public void Bind(TextureUnit unit = TextureUnit.Texture0)
    {
        _gl.ActiveTexture(unit);
        _gl.BindTexture(TextureTarget.Texture2D, Handle);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _gl.DeleteTexture(Handle);
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
