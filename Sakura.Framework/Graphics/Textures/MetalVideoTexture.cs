// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Threading;
using FFmpeg.AutoGen;
using Sakura.Framework.Graphics.Rendering.Metal;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// The Metal plane set for one video frame — the counterpart of <see cref="VideoGLTexture"/>. Under
/// <see cref="VideoPlaneLayout.Yuv420P"/> that is three single-channel R8 textures (Y, U, V); under
/// <see cref="VideoPlaneLayout.Nv12"/> it is a full-resolution R8 luma plane and a half-resolution
/// RG8 plane holding Cb and Cr interleaved. The video shader samples them and does YUV→RGB on the
/// GPU. Planes are sampled with the clamp-to-edge sampler (the native bridge selects it for plane
/// textures), matching GL's ClampToEdge wrap.
/// </summary>
public sealed class MetalVideoTexture : INativeVideoTexture
{
    private readonly nint device; // SakuraMetalDevice*

    private nint yHandle;

    /// <summary>
    /// Second plane: U under <see cref="VideoPlaneLayout.Yuv420P"/>, interleaved CbCr under
    /// <see cref="VideoPlaneLayout.Nv12"/>.
    /// </summary>
    private nint uHandle;

    /// <summary>
    /// Third plane (V). Zero under <see cref="VideoPlaneLayout.Nv12"/>, which has no third plane.
    /// </summary>
    private nint vHandle;

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

    private bool disposed;

    /// <summary>
    /// Accounts for every plane texture in <see cref="NativeMemoryTracker"/> until they are destroyed.
    /// </summary>
    private readonly NativeMemoryLease memoryLease;

    public MetalVideoTexture(nint device, int width, int height, VideoPlaneLayout layout = VideoPlaneLayout.Yuv420P)
    {
        this.device = device;
        Width = width;
        Height = height;
        Layout = layout;

        // Chroma is half-resolution (rounded up) in both layouts; they differ in how many textures
        // hold it, not in how much of it there is.
        int chromaWidth = (width + 1) / 2;
        int chromaHeight = (height + 1) / 2;

        yHandle = SakuraMetalNative.sakura_metal_create_plane_texture(device, width, height, 1);

        if (layout == VideoPlaneLayout.Nv12)
            uHandle = SakuraMetalNative.sakura_metal_create_plane_texture(device, chromaWidth, chromaHeight, 2);
        else
        {
            uHandle = SakuraMetalNative.sakura_metal_create_plane_texture(device, chromaWidth, chromaHeight, 1);
            vHandle = SakuraMetalNative.sakura_metal_create_plane_texture(device, chromaWidth, chromaHeight, 1);
        }

        if (yHandle == nint.Zero || uHandle == nint.Zero || (layout == VideoPlaneLayout.Yuv420P && vHandle == nint.Zero))
            throw new InvalidOperationException($"Failed to create Metal video plane textures ({width}x{height}, {layout}).");

        // All planes together, since they are allocated and freed as a unit. Single-channel R8 (plus
        // RG8 for NV12's chroma), so this is not the RGBA8 sizing the color textures use; both layouts
        // come to the same 12 bits per pixel. Taken after the check above, so a failed creation leaves
        // nothing on the books.
        memoryLease = NativeMemoryTracker.Add(NativeMemoryCategory.Video, NativeTextureMemory.BytesForVideoPlanes(width, height));
    }

    /// <summary>
    /// Binds the planes to fragment texture slots 0, 1 (and 2 for YUV420P), matching the video
    /// shader's samplers at [[texture(0/1/2)]]. Must be called on the draw thread.
    /// <paramref name="tiling"/> selects a repeating wrap (Tile fill) vs clamp-to-edge (normal video).
    /// </summary>
    public void BindPlanes(bool tiling)
    {
        Binds.Record();

        int repeat = tiling ? 1 : 0;
        SakuraMetalNative.sakura_metal_set_fragment_texture_wrap(device, yHandle, 0, repeat);
        SakuraMetalNative.sakura_metal_set_fragment_texture_wrap(device, uHandle, 1, repeat);

        if (Layout == VideoPlaneLayout.Yuv420P)
            SakuraMetalNative.sakura_metal_set_fragment_texture_wrap(device, vHandle, 2, repeat);
    }

    /// <summary>
    /// Uploads a decoded frame into the planes. Each plane's FFmpeg linesize (which may exceed the
    /// plane width due to row padding) is passed as the source stride. Must be called on the render
    /// thread.
    /// </summary>
    public unsafe void Upload(AVFrame* frame)
    {
        int width = frame->width;
        int height = frame->height;
        int chromaWidth = (width + 1) / 2;
        int chromaHeight = (height + 1) / 2;

        SakuraMetalNative.sakura_metal_upload_plane(yHandle, frame->data[0], width, height, frame->linesize[0]);

        if (Layout == VideoPlaneLayout.Nv12)
        {
            // chromaWidth is in texels and linesize[1] is in bytes; the native side takes both as-is
            // because Metal derives bytes-per-texel from the RG8 texture itself.
            SakuraMetalNative.sakura_metal_upload_plane(uHandle, frame->data[1], chromaWidth, chromaHeight, frame->linesize[1]);
        }
        else
        {
            SakuraMetalNative.sakura_metal_upload_plane(uHandle, frame->data[1], chromaWidth, chromaHeight, frame->linesize[1]);
            SakuraMetalNative.sakura_metal_upload_plane(vHandle, frame->data[2], chromaWidth, chromaHeight, frame->linesize[2]);
        }

        MarkAvailable();
    }

    /// <summary>
    /// Marks the texture as having valid data. Called after a successful upload.
    /// </summary>
    public void MarkAvailable() => Volatile.Write(ref available, true);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        if (yHandle != nint.Zero) { SakuraMetalNative.sakura_metal_destroy_texture(yHandle); yHandle = nint.Zero; }
        if (uHandle != nint.Zero) { SakuraMetalNative.sakura_metal_destroy_texture(uHandle); uHandle = nint.Zero; }
        if (vHandle != nint.Zero) { SakuraMetalNative.sakura_metal_destroy_texture(vHandle); vHandle = nint.Zero; }

        memoryLease?.Dispose();

        GC.SuppressFinalize(this);
    }
}
