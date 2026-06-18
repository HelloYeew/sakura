// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using FFmpeg.AutoGen;
using Silk.NET.OpenGL;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// Holds three single-channel GL textures representing the Y, U, V planes of a YUV420P frame.
/// Used exclusively by the GL video pipeline — the video shader samples all three planes and
/// performs YUV→RGB conversion on the GPU.
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
public sealed class VideoGLTexture : INativeVideoTexture
{
    public uint YHandle { get; private set; }
    public uint UHandle { get; private set; }
    public uint VHandle { get; private set; }

    /// <summary>
    /// Whether the texture has been uploaded at least once and is ready to draw.
    /// Written on the draw thread, read on the update thread — must use Volatile.
    /// </summary>
    public bool Available => Volatile.Read(ref available);
    private bool available;

    public int Width { get; }
    public int Height { get; }

    private readonly GL gl;
    private bool disposed;

    public VideoGLTexture(GL gl, int width, int height)
    {
        this.gl = gl;
        Width = width;
        Height = height;

        YHandle = gl.GenTexture();
        UHandle = gl.GenTexture();
        VHandle = gl.GenTexture();
    }

    /// <summary>
    /// Binds the Y, U, V planes to texture units 0, 1, 2 respectively.
    /// Must be called on the draw thread. Keeps GL calls inside this layer
    /// so higher-level code (VideoDrawNode) doesn't need a GL reference.
    /// </summary>
    public void BindPlanes(bool tiling)
    {
        int wrap = tiling ? (int)TextureWrapMode.Repeat : (int)TextureWrapMode.ClampToEdge;

        bindPlane(TextureUnit.Texture0, YHandle, wrap);
        bindPlane(TextureUnit.Texture1, UHandle, wrap);
        bindPlane(TextureUnit.Texture2, VHandle, wrap);
    }

    private void bindPlane(TextureUnit unit, uint handle, int wrap)
    {
        gl.ActiveTexture(unit);
        gl.BindTexture(TextureTarget.Texture2D, handle);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, wrap);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, wrap);
    }

    /// <summary>
    /// Uploads a decoded YUV420P frame into the Y, U, V planes.
    /// Must be called on the render thread.
    /// </summary>
    public unsafe void Upload(AVFrame* frame)
    {
        int width = frame->width;
        int height = frame->height;
        int chromaWidth = (width + 1) / 2;
        int chromaHeight = (height + 1) / 2;

        uploadPlane(YHandle, frame->data[0], frame->linesize[0], width, height);
        uploadPlane(UHandle, frame->data[1], frame->linesize[1], chromaWidth, chromaHeight);
        uploadPlane(VHandle, frame->data[2], frame->linesize[2], chromaWidth, chromaHeight);
        MarkAvailable();
    }

    private unsafe void uploadPlane(uint handle, byte* data, int linesize, int width, int height)
    {
        gl.BindTexture(TextureTarget.Texture2D, handle);
        gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        gl.PixelStore(PixelStoreParameter.UnpackRowLength, linesize);

        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.R8, (uint)width, (uint)height, 0, PixelFormat.Red, PixelType.UnsignedByte, data);

        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        gl.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
    }

    /// <summary>
    /// Marks the texture as having valid data. Called after a successful upload.
    /// </summary>
    public void MarkAvailable() => Volatile.Write(ref available, true);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        gl.DeleteTexture(YHandle);
        gl.DeleteTexture(UHandle);
        gl.DeleteTexture(VHandle);

        GC.SuppressFinalize(this);
    }
}
