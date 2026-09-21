// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Threading;
using FFmpeg.AutoGen;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Logging;
using Sakura.Framework.Statistic;
using Vortice.Direct3D11;
using Vortice.DXGI;

// FFmpeg.AutoGen also declares ID3D11* interop types; alias the D3D ones to Vortice's.
using ID3D11Device = Vortice.Direct3D11.ID3D11Device;
using ID3D11DeviceContext = Vortice.Direct3D11.ID3D11DeviceContext;
using ID3D11Texture2D = Vortice.Direct3D11.ID3D11Texture2D;

namespace Sakura.Framework.Graphics.Rendering.Direct3D11;

/// <summary>
/// The zero-copy counterpart of <see cref="D3D11VideoTexture"/>: instead of allocating plane textures
/// and copying a decoded frame into them, this binds two shader resource views onto the slice of
/// D3D11VA's own NV12 texture array that the decoder wrote — an <c>R8_UNORM</c> view for luma and an
/// <c>R8G8_UNORM</c> view for the interleaved chroma.
/// </summary>
public sealed unsafe class D3D11HardwareVideoTexture : INativeVideoTexture
{
    private readonly ID3D11Device device;
    private readonly ID3D11DeviceContext context;

    /// <summary>
    /// Our own reference on the decoded frame, which is what stops FFmpeg recycling the texture-array
    /// slice underneath a frame still being displayed. Replaced on each upload, released on disposal.
    /// </summary>
    private AVFrame* heldFrame;

    /// <summary>
    /// Views onto the decoder's array, keyed by slice. The array is allocated once per stream and the
    /// decoder cycles its slices, so every view here is reused many times over — creating a pair per
    /// frame would be a per-frame allocation to describe memory that has not changed.
    /// </summary>
    private readonly Dictionary<int, (ID3D11ShaderResourceView Luma, ID3D11ShaderResourceView Chroma)> sliceViews = new();

    /// <summary>
    /// The array the cached views point into. If the decoder is reopened, it allocates a new array, and
    /// every view cached against the old one has to go.
    /// </summary>
    private nint cachedArray;

    private ID3D11ShaderResourceView? lumaSrv;
    private ID3D11ShaderResourceView? chromaSrv;

    private ID3D11SamplerState clampSampler;
    private ID3D11SamplerState repeatSampler;

    /// <summary>
    /// Frames whose views could not be created, so nothing was bound. Non-zero here means video is
    /// frozen on its previous frame, which is the visible symptom to look for.
    /// </summary>
    private static readonly GlobalStatistic<long> stat_map_failures =
        GlobalStatistics.Get<long>("Video", "Zero-Copy Map Failures", StatisticKind.Cumulative);

    public int Width { get; }
    public int Height { get; }

    /// <summary>
    /// Zero: the planes are views onto the decoder's own texture array. Charging for them would
    /// double-count D3D11VA's frame pool and make the video total grow when this path made it shrink.
    /// </summary>
    public long PlaneBytes => 0;

    /// <summary>
    /// Always NV12 — the renderer only routes a frame here once it has checked the decoder's array is
    /// in that format.
    /// </summary>
    public VideoPlaneLayout Layout => VideoPlaneLayout.Nv12;

    public bool Available => Volatile.Read(ref available);
    private bool available;

    public TextureBindCounter Binds { get; } = new TextureBindCounter();

    private bool disposed;

    public D3D11HardwareVideoTexture(ID3D11Device device, ID3D11DeviceContext context, int width, int height)
    {
        this.device = device;
        this.context = context;
        Width = width;
        Height = height;

        heldFrame = ffmpeg.av_frame_alloc();

        clampSampler = createSampler(TextureAddressMode.Clamp);
        repeatSampler = createSampler(TextureAddressMode.Wrap);

        // No NativeMemoryTracker lease: the planes belong to the decoder's frame pool, and borrowed
        // memory is not ours to charge for.
    }

    private ID3D11SamplerState createSampler(TextureAddressMode mode) =>
        device.CreateSamplerState(new SamplerDescription
        {
            Filter = Filter.MinMagMipLinear,
            AddressU = mode,
            AddressV = mode,
            AddressW = mode,
            ComparisonFunc = ComparisonFunction.Never,
            MinLOD = 0,
            MaxLOD = float.MaxValue,
        });

    /// <summary>
    /// Binds the two plane views and their sampler to slots 0 and 1 on the pixel stage, matching
    /// <c>video_nv12.frag</c>'s <c>t0</c>/<c>t1</c>. Must be called on the draw thread.
    /// <paramref name="tiling"/> selects the repeating vs clamp sampler.
    /// </summary>
    public void BindPlanes(bool tiling)
    {
        Binds.Record();

        if (lumaSrv == null || chromaSrv == null)
            return;

        var sampler = tiling ? repeatSampler : clampSampler;

        context.PSSetShaderResources(0, new[] { lumaSrv, chromaSrv });
        context.PSSetSamplers(0, new[] { sampler, sampler });
    }

    /// <summary>
    /// Points this texture at the frame's slice of the decoder's array. <c>data[0]</c> is the
    /// <c>ID3D11Texture2D*</c> holding every in-flight frame and <c>data[1]</c> is this frame's index
    /// into it. Must be called on the draw thread. Nothing is copied.
    /// </summary>
    public void Upload(AVFrame* frame)
    {
        nint array = (nint)frame->data[0];
        int slice = (int)frame->data[1];

        if (array == nint.Zero)
        {
            mapFailed("frame carried no ID3D11Texture2D");
            return;
        }

        // A reopened codec allocates a fresh array, so views cached against the old one describe memory
        // that is no longer ours to read.
        if (array != cachedArray)
        {
            clearSliceViews();
            cachedArray = array;
        }

        if (!tryGetViews(array, slice, out var views))
        {
            mapFailed($"could not create shader resource views for slice {slice} of {frame->width}x{frame->height}");
            return;
        }

        // Taken before the previous reference is dropped, so the pool can never see this texture
        // holding nothing. Ordering matters: av_frame_ref takes a reference on the array slice, which
        // is what stops the decoder writing a new frame into it while this one is on screen.
        ffmpeg.av_frame_unref(heldFrame);

        if (ffmpeg.av_frame_ref(heldFrame, frame) < 0)
        {
            mapFailed("av_frame_ref failed; refusing to draw a slice the decoder may reuse");
            return;
        }

        lumaSrv = views.Luma;
        chromaSrv = views.Chroma;

        MarkAvailable();
    }

    /// <summary>
    /// Fetches the cached view pair for a slice, creating it on first use.
    /// </summary>
    private bool tryGetViews(nint array, int slice, out (ID3D11ShaderResourceView Luma, ID3D11ShaderResourceView Chroma) views)
    {
        if (sliceViews.TryGetValue(slice, out views))
            return true;

        // Wrapped without taking ownership of the decoder's reference: the AddRef here balances the
        // Dispose below. The views themselves keep the underlying resource alive independently.
        using var texture = new ID3D11Texture2D(array);
        texture.AddRef();

        ID3D11ShaderResourceView? luma = null;
        ID3D11ShaderResourceView? chroma = null;

        try
        {
            // One slice of the array, viewed twice. The NV12 array has no separate chroma subresource —
            // the plane split is expressed purely by the view format, R8 reading the luma plane and R8G8
            // the interleaved chroma at half resolution.
            luma = device.CreateShaderResourceView(texture, new ShaderResourceViewDescription
            {
                Format = Format.R8_UNorm,
                ViewDimension = Vortice.Direct3D.ShaderResourceViewDimension.Texture2DArray,
                Texture2DArray = new Texture2DArrayShaderResourceView
                {
                    MostDetailedMip = 0,
                    MipLevels = 1,
                    FirstArraySlice = (uint)slice,
                    ArraySize = 1,
                },
            });

            chroma = device.CreateShaderResourceView(texture, new ShaderResourceViewDescription
            {
                Format = Format.R8G8_UNorm,
                ViewDimension = Vortice.Direct3D.ShaderResourceViewDimension.Texture2DArray,
                Texture2DArray = new Texture2DArrayShaderResourceView
                {
                    MostDetailedMip = 0,
                    MipLevels = 1,
                    FirstArraySlice = (uint)slice,
                    ArraySize = 1,
                },
            });
        }
        catch (Exception e)
        {
            // Creation throws rather than returning null in Vortice. The likely cause is the frames
            // context having been initialised without D3D11_BIND_SHADER_RESOURCE, which makes the
            // array unbindable no matter how the view is described.
            Logger.Error($"[D3D11HardwareVideoTexture] Shader resource view creation failed: {e.Message}");
            luma?.Dispose();
            chroma?.Dispose();
            views = default;
            return false;
        }

        views = (luma!, chroma!);
        sliceViews[slice] = views;
        return true;
    }

    /// <summary>
    /// Records a frame this texture could not take. Availability is left as it was: if a previous frame
    /// is still bound, the sprite keeps drawing it, which is a freeze rather than a corrupt picture.
    /// </summary>
    private void mapFailed(string reason)
    {
        stat_map_failures.Value++;
        Logger.Warning($"[D3D11HardwareVideoTexture] Zero-copy map failed: {reason}.");
    }

    private void clearSliceViews()
    {
        foreach (var (luma, chroma) in sliceViews.Values)
        {
            luma.Dispose();
            chroma.Dispose();
        }

        sliceViews.Clear();
        lumaSrv = null;
        chromaSrv = null;
    }

    public void MarkAvailable() => Volatile.Write(ref available, true);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        clearSliceViews();
        cachedArray = nint.Zero;

        if (heldFrame != null)
        {
            var f = heldFrame;
            ffmpeg.av_frame_free(&f);
            heldFrame = null;
        }

        clampSampler?.Dispose(); clampSampler = null!;
        repeatSampler?.Dispose(); repeatSampler = null!;

        Volatile.Write(ref available, false);

        GC.SuppressFinalize(this);
    }
}
