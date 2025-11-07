// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.Diagnostics.CodeAnalysis;
using Silk.NET.OpenGL;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// The actual texture loaded in OpenGL.
/// <remarks>The drawable should use the public-facing <see cref="Texture"/> which may point to a region of this TextureGL</remarks>
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
public class GLTexture : IDisposable
{
    public uint Handle { get; }
    public int Width { get; }
    public int Height { get; }

    private readonly GL gl;
    private bool disposed;

    public static GLTexture WhitePixel { get; private set; }

    public GLTexture(GL gl, int width, int height, ReadOnlySpan<byte> data)
    {
        this.gl = gl;
        Width = width;
        Height = height;

        Handle = this.gl.GenTexture();
        Bind();

        this.gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, data);
        this.gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        this.gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        this.gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        this.gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
    }

    public static void CreateWhitePixel(GL gl)
    {
        if (WhitePixel == null)
        {
            byte[] whitePixelData = { 255, 255, 255, 255 };
            WhitePixel = new GLTexture(gl, 1, 1, whitePixelData);
        }
    }

    public void Bind(TextureUnit unit = TextureUnit.Texture0)
    {
        gl.ActiveTexture(unit);
        gl.BindTexture(TextureTarget.Texture2D, Handle);
    }

    public void Dispose()
    {
        if (disposed) return;
        gl.DeleteTexture(Handle);
        disposed = true;
        GC.SuppressFinalize(this);
    }
}
