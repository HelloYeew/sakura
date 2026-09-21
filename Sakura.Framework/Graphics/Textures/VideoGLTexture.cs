// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using FFmpeg.AutoGen;
using Sakura.Framework.Statistic;
using Silk.NET.OpenGL;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// The GL plane set for one video frame: three single-channel textures for
/// <see cref="VideoPlaneLayout.Yuv420P"/>, or a single-channel luma plus a two-channel interleaved
/// chroma texture for <see cref="VideoPlaneLayout.Nv12"/>. The video shader samples them and does
/// YUV→RGB on the GPU.
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
public sealed class VideoGLTexture : INativeVideoTexture
{
    public uint YHandle { get; private set; }

    /// <summary>
    /// Second plane: U under <see cref="VideoPlaneLayout.Yuv420P"/>, interleaved CbCr under
    /// <see cref="VideoPlaneLayout.Nv12"/>.
    /// </summary>
    public uint UHandle { get; private set; }

    /// <summary>
    /// Third plane (V). Zero under <see cref="VideoPlaneLayout.Nv12"/>, which has no third plane.
    /// </summary>
    public uint VHandle { get; private set; }

    public VideoPlaneLayout Layout { get; }

    /// <summary>
    /// Whether the texture has been uploaded at least once and is ready to draw.
    /// Written on the draw thread, read on the update thread — must use Volatile.
    /// </summary>
    public bool Available => Volatile.Read(ref available);
    private bool available;

    public TextureBindCounter Binds { get; } = new TextureBindCounter();

    public int Width { get; }
    public int Height { get; }

    /// <summary>
    /// 1.5 bytes per pixel across the plane set — the same figure this texture's memory lease is taken
    /// for, so the tracker and anything displaying a size agree.
    /// </summary>
    public long PlaneBytes => NativeTextureMemory.BytesForVideoPlanes(Width, Height);

    private readonly GL gl;
    private bool disposed;

    /// <summary>
    /// Whether each plane's storage has been allocated. Storage is specified once with
    /// <c>glTexImage2D</c> and then written with <c>glTexSubImage2D</c>; re-specifying it every frame
    /// makes the driver re-validate (and potentially reallocate) the texture for no reason.
    /// </summary>
    private bool storageAllocated;

    /// <summary>
    /// Accounts for every plane texture in <see cref="NativeMemoryTracker"/> until they are destroyed.
    /// </summary>
    private readonly NativeMemoryLease memoryLease;

    public VideoGLTexture(GL gl, int width, int height, VideoPlaneLayout layout = VideoPlaneLayout.Yuv420P)
    {
        this.gl = gl;
        Width = width;
        Height = height;
        Layout = layout;

        YHandle = gl.GenTexture();
        UHandle = gl.GenTexture();

        if (layout == VideoPlaneLayout.Yuv420P)
            VHandle = gl.GenTexture();

        // Both layouts carry 12 bits per pixel, so the accounting is the same either way.
        memoryLease = NativeMemoryTracker.Add(NativeMemoryCategory.Video, NativeTextureMemory.BytesForVideoPlanes(width, height));
    }

    /// <summary>
    /// Binds the planes to texture units 0, 1 (and 2 for YUV420P).
    /// Must be called on the draw thread. Keeps GL calls inside this layer
    /// so higher-level code (VideoDrawNode) doesn't need a GL reference.
    /// </summary>
    public void BindPlanes(bool tiling)
    {
        Binds.Record();

        int wrap = tiling ? (int)TextureWrapMode.Repeat : (int)TextureWrapMode.ClampToEdge;

        bindPlane(TextureUnit.Texture0, YHandle, wrap);
        bindPlane(TextureUnit.Texture1, UHandle, wrap);

        if (Layout == VideoPlaneLayout.Yuv420P)
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
    /// Uploads a decoded frame into the planes. Must be called on the render thread.
    /// </summary>
    public unsafe void Upload(AVFrame* frame)
    {
        int width = frame->width;
        int height = frame->height;
        int chromaWidth = (width + 1) / 2;
        int chromaHeight = (height + 1) / 2;

        if (!storageAllocated)
        {
            allocatePlane(YHandle, InternalFormat.R8, width, height);

            if (Layout == VideoPlaneLayout.Nv12)
                allocatePlane(UHandle, InternalFormat.RG8, chromaWidth, chromaHeight);
            else
            {
                allocatePlane(UHandle, InternalFormat.R8, chromaWidth, chromaHeight);
                allocatePlane(VHandle, InternalFormat.R8, chromaWidth, chromaHeight);
            }

            storageAllocated = true;
        }

        uploadPlane(YHandle, frame->data[0], frame->linesize[0], width, height, PixelFormat.Red);

        if (Layout == VideoPlaneLayout.Nv12)
        {
            // linesize[1] counts bytes, and this plane is two bytes per texel — UnpackRowLength wants
            // texels, so the stride is halved rather than passed through.
            uploadPlane(UHandle, frame->data[1], frame->linesize[1] / 2, chromaWidth, chromaHeight, PixelFormat.RG);
        }
        else
        {
            uploadPlane(UHandle, frame->data[1], frame->linesize[1], chromaWidth, chromaHeight, PixelFormat.Red);
            uploadPlane(VHandle, frame->data[2], frame->linesize[2], chromaWidth, chromaHeight, PixelFormat.Red);
        }

        MarkAvailable();
    }

    /// <summary>
    /// Specifies a plane's storage and the sampling state that never changes per frame. Runs once per
    /// plane, not once per frame.
    /// </summary>
    private unsafe void allocatePlane(uint handle, InternalFormat internalFormat, int width, int height)
    {
        gl.BindTexture(TextureTarget.Texture2D, handle);

        var format = internalFormat == InternalFormat.RG8 ? PixelFormat.RG : PixelFormat.Red;
        gl.TexImage2D(TextureTarget.Texture2D, 0, internalFormat, (uint)width, (uint)height, 0, format, PixelType.UnsignedByte, null);

        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

        // Wrap is re-set per bind by BindPlanes, which is the only thing that knows the fill mode;
        // these are just a defined starting state.
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
    }

    private unsafe void uploadPlane(uint handle, byte* data, int rowLength, int width, int height, PixelFormat format)
    {
        if (data == null || width <= 0 || height <= 0)
            return;

        gl.BindTexture(TextureTarget.Texture2D, handle);
        gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        gl.PixelStore(PixelStoreParameter.UnpackRowLength, rowLength);

        gl.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, (uint)width, (uint)height, format, PixelType.UnsignedByte, data);

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

        if (VHandle != 0)
            gl.DeleteTexture(VHandle);

        memoryLease?.Dispose();

        GC.SuppressFinalize(this);
    }
}
