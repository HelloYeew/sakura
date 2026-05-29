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
    public uint Handle { get; private set; }
    public int Width { get; }
    public int Height { get; }

    public bool Available { get; private set; }

    private readonly GL gl;
    private bool disposed;

    public static GLTexture WhitePixel { get; private set; }

    public GLTexture(GL gl, int width, int height)
    {
        this.gl = gl;
        Width = width;
        Height = height;
        Handle = gl.GenTexture();
    }

    /// <summary>
    /// Change the texture wrap mode (for tiling/repeating).
    /// </summary>
    /// <param name="mode">The wrap mode to set.</param>
    public void SetWrapMode(TextureWrapMode mode)
    {
        if (gl == null || disposed) return;

        gl.GetInteger(GLEnum.TextureBinding2D, out int currentlyBoundTexture);

        Bind();
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)mode);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)mode);

        if (currentlyBoundTexture != Handle && currentlyBoundTexture > 0)
        {
            gl.BindTexture(TextureTarget.Texture2D, (uint)currentlyBoundTexture);
        }
    }

    public void Upload(ReadOnlySpan<byte> data)
    {
        if (disposed) return;

        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(TextureTarget.Texture2D, Handle);

        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Srgb8Alpha8, (uint)Width, (uint)Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, data);
        gl.GenerateMipmap(TextureTarget.Texture2D);

        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

        Available = true;
    }

    public static void CreateWhitePixel(GL gl)
    {
        if (WhitePixel == null)
        {
            byte[] whitePixelData = { 255, 255, 255, 255 };
            WhitePixel = new GLTexture(gl, 1, 1);
            WhitePixel.Upload(whitePixelData); // can upload directly because this is initialized on the main thread
        }
    }

    public void Bind(TextureUnit unit = TextureUnit.Texture0)
    {
        gl.ActiveTexture(unit);

        if (!Available && WhitePixel != null)
        {
            gl.BindTexture(TextureTarget.Texture2D, WhitePixel.Handle);
            return;
        }

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
