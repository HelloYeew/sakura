// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using FFmpeg.AutoGen;
using Sakura.Framework.Graphics.Textures;
using Silk.NET.OpenGL;

namespace Sakura.Framework.Graphics.Video;

/// <summary>
/// Uploads a decoded YUV420P frame into a <see cref="VideoGLTexture"/>'s three single-channel planes
/// without any CPU-side pixel conversion. The video shader performs YUV→RGB on the GPU.
/// Disposing this upload returns the underlying <see cref="FFmpegFrame"/> to its pool.
/// </summary>
public sealed unsafe class VideoTextureUpload : IDisposable
{
    private readonly FFmpegFrame frame;
    private bool disposed;

    public AVFrame* Frame => frame.Pointer;

    public int Width => Frame->width;
    public int Height => Frame->height;
    public int ChromaWidth => (Frame->width + 1) / 2;
    public int ChromaHeight => (Frame->height + 1) / 2;

    internal VideoTextureUpload(FFmpegFrame frame)
    {
        this.frame = frame;
    }

    /// <summary>
    /// Uploads Y, U, V planes into a <see cref="VideoGLTexture"/>. Must be called from the GL thread.
    /// </summary>
    public void Upload(GL gl, VideoGLTexture target)
    {
        uploadPlane(gl, target.YHandle, Frame->data[0], Frame->linesize[0], Width, Height);
        uploadPlane(gl, target.UHandle, Frame->data[1], Frame->linesize[1], ChromaWidth, ChromaHeight);
        uploadPlane(gl, target.VHandle, Frame->data[2], Frame->linesize[2], ChromaWidth, ChromaHeight);
        target.MarkAvailable();
    }

    private static void uploadPlane(GL gl, uint handle, byte* data, int linesize, int width, int height)
    {
        gl.BindTexture(TextureTarget.Texture2D, handle);
        gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        gl.PixelStore(PixelStoreParameter.UnpackRowLength, linesize);

        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.R8,
            (uint)width, (uint)height, 0,
            PixelFormat.Red, PixelType.UnsignedByte, data);

        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        gl.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        frame.Return();
    }
}
