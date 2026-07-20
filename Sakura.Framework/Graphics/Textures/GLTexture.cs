// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.Diagnostics.CodeAnalysis;
using Silk.NET.OpenGL;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// The OpenGL-backed implementation of <see cref="INativeTexture"/>.
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
public class GLTexture : INativeTexture
{
    public uint GLHandle { get; private set; }
    public nint Handle => (nint)GLHandle;

    public int Width { get; }
    public int Height { get; }

    public bool Available { get; private set; }

    private readonly GL gl;
    private bool disposed;

    private bool mipmapsDirty;

    public static GLTexture WhitePixel { get; private set; }

    /// <summary>
    /// Creates the shared 1×1 white pixel texture if it doesn't exist yet.
    /// Must be called on the draw thread after the GL context is current.
    /// </summary>
    public static void CreateWhitePixel(GL gl)
    {
        if (WhitePixel != null) return;

        byte[] whitePixelData = { 255, 255, 255, 255 };
        WhitePixel = new GLTexture(gl, 1, 1);
        WhitePixel.Upload(whitePixelData);
    }

    public GLTexture(GL gl, int width, int height)
    {
        this.gl = gl;
        Width = width;
        Height = height;
    }

    public void SetWrapMode(TextureWrapMode mode)
    {
        if (gl == null || disposed) return;

        gl.GetInteger(GLEnum.TextureBinding2D, out int currentlyBoundTexture);
        Bind();
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)mode);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)mode);
        if (currentlyBoundTexture > 0 && (uint)currentlyBoundTexture != GLHandle)
            gl.BindTexture(TextureTarget.Texture2D, (uint)currentlyBoundTexture);
    }

    public void Upload(ReadOnlySpan<byte> data)
    {
        if (disposed) return;

        if (GLHandle == 0)
        {
            GLHandle = gl.GenTexture();
        }

        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(TextureTarget.Texture2D, GLHandle);

        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Srgb8Alpha8, (uint)Width, (uint)Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, data);
        mipmapsDirty = true;

        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

        Available = true;
    }

    public void UploadRegion(int x, int y, int width, int height, ReadOnlySpan<byte> data)
    {
        if (disposed || GLHandle == 0) return;

        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(TextureTarget.Texture2D, GLHandle);
        gl.TexSubImage2D(TextureTarget.Texture2D, 0, x, y, (uint)width, (uint)height, PixelFormat.Rgba, PixelType.UnsignedByte, data);
        mipmapsDirty = true;
    }

    public void Bind(int slot = 0)
    {
        gl.ActiveTexture(TextureUnit.Texture0 + slot);

        if (!Available || disposed || GLHandle == 0)
        {
            if (WhitePixel != null)
                gl.BindTexture(TextureTarget.Texture2D, WhitePixel.GLHandle);
            return;
        }

        gl.BindTexture(TextureTarget.Texture2D, GLHandle);
        
        if (mipmapsDirty)
        {
            gl.GenerateMipmap(TextureTarget.Texture2D);
            mipmapsDirty = false;
        }
    }

    /// <summary>
    /// Creates a texture suitable as a framebuffer color attachment: storage allocated up
    /// front, linear filtering without mipmaps (render targets have none), clamped wrapping.
    /// Must be called on the draw thread.
    /// </summary>
    internal static unsafe GLTexture CreateRenderTarget(GL gl, int width, int height, bool pixelSnapping = false)
    {
        var texture = new GLTexture(gl, width, height);
        texture.GLHandle = gl.GenTexture();

        gl.GetInteger(GLEnum.TextureBinding2D, out int previouslyBound);

        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(TextureTarget.Texture2D, texture.GLHandle);

        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Srgb8Alpha8, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, (void*)null);

        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, pixelSnapping ? (int)TextureMinFilter.Nearest : (int)TextureMinFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, pixelSnapping ? (int)TextureMagFilter.Nearest : (int)TextureMagFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        texture.Available = true;

        if (previouslyBound > 0)
            gl.BindTexture(TextureTarget.Texture2D, (uint)previouslyBound);

        return texture;
    }

    public void Dispose()
    {
        if (disposed) return;

        if (GLHandle != 0)
        {
            // Scrub the renderer's slot tracking first: GL recycles handle IDs, so a
            // future texture could alias this handle and be mistaken for already-bound.
            Rendering.GLRenderer.NotifyTextureDeleted(GLHandle);
            gl.DeleteTexture(GLHandle);
        }

        disposed = true;
        GC.SuppressFinalize(this);
    }
}
