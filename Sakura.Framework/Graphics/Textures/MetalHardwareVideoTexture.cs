// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Threading;
using FFmpeg.AutoGen;
using Sakura.Framework.Graphics.Rendering.Metal;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// The zero-copy counterpart of <see cref="MetalVideoTexture"/>: instead of allocating plane textures
/// and copying a decoded frame into them, this borrows the <c>CVPixelBuffer</c> VideoToolbox already
/// decoded into and hands its two <see cref="VideoPlaneLayout.Nv12"/> planes to Metal as views onto the
/// same IOSurface. There is no upload — <see cref="Upload"/> is two
/// <c>CVMetalTextureCacheCreateTextureFromImage</c> calls, and the pixels never move.
/// </summary>
public sealed class MetalHardwareVideoTexture : INativeVideoTexture
{
    private readonly nint device; // SakuraMetalDevice*

    /// <summary>
    /// Full-resolution single-channel luma plane, as an R8 view onto plane 0 of the pixel buffer.
    /// Zero until the first frame lands.
    /// </summary>
    private nint yHandle;

    /// <summary>
    /// Half-resolution two-channel plane holding Cb and Cr interleaved, as an RG8 view onto plane 1.
    /// </summary>
    private nint cbCrHandle;

    /// <summary>
    /// A frame whose planes could not be mapped, so nothing was bound. Non-zero here means video is
    /// frozen on its previous frame, which is the visible symptom to look for.
    /// </summary>
    private static readonly Statistic.GlobalStatistic<long> stat_map_failures =
        Statistic.GlobalStatistics.Get<long>("Video", "Zero-Copy Map Failures", Statistic.StatisticKind.Cumulative);

    public VideoPlaneLayout Layout => VideoPlaneLayout.Nv12;

    /// <summary>
    /// Whether a frame has been mapped and is ready to draw. Written on the draw thread, read on the
    /// update thread — must use Volatile.
    /// </summary>
    public bool Available => Volatile.Read(ref available);
    private bool available;

    public TextureBindCounter Binds { get; } = new TextureBindCounter();

    public int Width { get; }
    public int Height { get; }

    /// <summary>
    /// Zero: the planes are views onto a frame VideoToolbox owns. Charging for them would double-count
    /// the decoder's own pool and make the video total grow when this path made it shrink.
    /// </summary>
    public long PlaneBytes => 0;

    private bool disposed;

    public MetalHardwareVideoTexture(nint device, int width, int height)
    {
        this.device = device;
        Width = width;
        Height = height;

        // Nothing is allocated here. The planes arrive with the first frame and belong to VideoToolbox,
        // which is the whole point of this class — and why there is no NativeMemoryTracker lease:
        // borrowed memory is not ours to charge for.
    }

    /// <summary>
    /// Binds the two planes to fragment texture slots 0 and 1, matching <c>video_nv12.frag</c>'s
    /// samplers at [[texture(0/1)]]. Must be called on the draw thread. <paramref name="tiling"/>
    /// selects a repeating wrap (Tile fill) vs clamp-to-edge (normal video).
    /// </summary>
    public void BindPlanes(bool tiling)
    {
        Binds.Record();

        if (yHandle == nint.Zero || cbCrHandle == nint.Zero)
            return;

        int repeat = tiling ? 1 : 0;
        SakuraMetalNative.sakura_metal_set_fragment_texture_wrap(device, yHandle, 0, repeat);
        SakuraMetalNative.sakura_metal_set_fragment_texture_wrap(device, cbCrHandle, 1, repeat);
    }

    /// <summary>
    /// Maps the frame's <c>CVPixelBuffer</c> (carried in <c>data[3]</c> of an
    /// <c>AV_PIX_FMT_VIDEOTOOLBOX</c> frame) as two plane textures. Must be called on the render
    /// thread. Nothing is copied.
    /// </summary>
    public unsafe void Upload(AVFrame* frame)
    {
        nint pixelBuffer = (nint)frame->data[3];

        if (pixelBuffer == nint.Zero)
        {
            mapFailed("frame carried no CVPixelBuffer");
            return;
        }

        // Mapped into locals and swapped in only once both succeeded. Releasing the old planes first
        // would, on a failure, leave this texture bound to nothing while still being drawn — the old
        // frame is a better thing to show than the previous *sprite's* texture slots.
        nint newY = SakuraMetalNative.sakura_metal_create_plane_texture_from_pixel_buffer(device, pixelBuffer, 0, 1);
        nint newCbCr = SakuraMetalNative.sakura_metal_create_plane_texture_from_pixel_buffer(device, pixelBuffer, 1, 2);

        if (newY == nint.Zero || newCbCr == nint.Zero)
        {
            // The decoder probes the format before choosing this path, so reaching here means a
            // mid-stream change to something unmappable (10-bit, 4:2:2) or a texture cache failure.
            if (newY != nint.Zero) SakuraMetalNative.sakura_metal_destroy_texture(newY);
            if (newCbCr != nint.Zero) SakuraMetalNative.sakura_metal_destroy_texture(newCbCr);

            mapFailed($"CVMetalTextureCache returned nothing for {frame->width}x{frame->height}");
            return;
        }

        // Only now are the previous frame's planes handed back. Destroying them does not release the
        // pixel buffer immediately — the native side defers that to the next frame's GPU completion —
        // so this is safe even though the previous frame may still be in flight.
        releasePlanes();

        yHandle = newY;
        cbCrHandle = newCbCr;

        MarkAvailable();
    }

    /// <summary>
    /// Records a frame this texture could not take. Availability is left as it was: if a previous frame
    /// is still mapped, the sprite keeps drawing it, which is a freeze rather than a corrupt picture.
    /// </summary>
    private void mapFailed(string reason)
    {
        stat_map_failures.Value++;
        Logger.Warning($"[MetalHardwareVideoTexture] Zero-copy map failed: {reason}.");
    }

    /// <summary>
    /// Destroys the current plane views. The native side releases the CoreVideo objects behind them
    ///  once the GPU has passed this frame, not here. Draw thread only.
    /// </summary>
    private void releasePlanes()
    {
        if (yHandle != nint.Zero) { SakuraMetalNative.sakura_metal_destroy_texture(yHandle); yHandle = nint.Zero; }
        if (cbCrHandle != nint.Zero) { SakuraMetalNative.sakura_metal_destroy_texture(cbCrHandle); cbCrHandle = nint.Zero; }
    }

    public void MarkAvailable() => Volatile.Write(ref available, true);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        releasePlanes();
        Volatile.Write(ref available, false);

        GC.SuppressFinalize(this);
    }
}
