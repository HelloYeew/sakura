// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Threading;
using FFmpeg.AutoGen;
using Sakura.Framework.Graphics.Rendering.Metal;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// Holds three single-channel R8 Metal textures representing the Y, U, V planes of a YUV420P frame —
/// the Metal counterpart of <see cref="VideoGLTexture"/>. The video shader samples all three planes
/// and performs YUV -> RGB conversion on the GPU. Planes are sampled with the clamp-to-edge sampler (the
/// native bridge selects it for plane textures), matching GL's ClampToEdge wrap.
/// </summary>
public sealed class MetalVideoTexture : INativeVideoTexture
{
    private readonly nint device; // SakuraMetalDevice*

    private nint yHandle;
    private nint uHandle;
    private nint vHandle;

    /// <summary>
    /// Whether the texture has been uploaded at least once and is ready to draw.
    /// Written on the draw thread, read on the update thread — must use Volatile.
    /// </summary>
    public bool Available => Volatile.Read(ref available);
    private bool available;

    public int Width { get; }
    public int Height { get; }

    private bool disposed;

    public MetalVideoTexture(nint device, int width, int height)
    {
        this.device = device;
        Width = width;
        Height = height;

        // YUV420P: chroma planes are half-resolution (rounded up), matching VideoGLTexture's sizing.
        int chromaWidth = (width + 1) / 2;
        int chromaHeight = (height + 1) / 2;

        yHandle = SakuraMetalNative.sakura_metal_create_plane_texture(device, width, height);
        uHandle = SakuraMetalNative.sakura_metal_create_plane_texture(device, chromaWidth, chromaHeight);
        vHandle = SakuraMetalNative.sakura_metal_create_plane_texture(device, chromaWidth, chromaHeight);

        if (yHandle == nint.Zero || uHandle == nint.Zero || vHandle == nint.Zero)
            throw new InvalidOperationException($"Failed to create Metal video plane textures ({width}x{height}).");
    }

    /// <summary>
    /// Binds the Y, U, V planes to fragment texture slots 0, 1, 2 respectively (matching the video
    /// shader's u_TextureY/U/V at [[texture(0/1/2)]]). Must be called on the draw thread.
    /// <paramref name="tiling"/> selects a repeating wrap (Tile fill) vs clamp-to-edge (normal video).
    /// </summary>
    public void BindPlanes(bool tiling)
    {
        int repeat = tiling ? 1 : 0;
        SakuraMetalNative.sakura_metal_set_fragment_texture_wrap(device, yHandle, 0, repeat);
        SakuraMetalNative.sakura_metal_set_fragment_texture_wrap(device, uHandle, 1, repeat);
        SakuraMetalNative.sakura_metal_set_fragment_texture_wrap(device, vHandle, 2, repeat);
    }

    /// <summary>
    /// Uploads a decoded YUV420P frame into the Y, U, V planes. Each plane's FFmpeg linesize (which may
    /// exceed the plane width due to row padding) is passed as the source stride. Must be called on the
    /// render thread.
    /// </summary>
    public unsafe void Upload(AVFrame* frame)
    {
        int width = frame->width;
        int height = frame->height;
        int chromaWidth = (width + 1) / 2;
        int chromaHeight = (height + 1) / 2;

        SakuraMetalNative.sakura_metal_upload_plane(yHandle, frame->data[0], width, height, frame->linesize[0]);
        SakuraMetalNative.sakura_metal_upload_plane(uHandle, frame->data[1], chromaWidth, chromaHeight, frame->linesize[1]);
        SakuraMetalNative.sakura_metal_upload_plane(vHandle, frame->data[2], chromaWidth, chromaHeight, frame->linesize[2]);

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

        GC.SuppressFinalize(this);
    }
}
