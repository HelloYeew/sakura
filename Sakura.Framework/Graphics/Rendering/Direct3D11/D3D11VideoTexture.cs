// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Threading;
using FFmpeg.AutoGen;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Statistic;
using Vortice.Direct3D11;
using Vortice.DXGI;

// FFmpeg.AutoGen also declares ID3D11* interop types; alias the D3D ones to Vortice's.
using ID3D11Device = Vortice.Direct3D11.ID3D11Device;
using ID3D11DeviceContext = Vortice.Direct3D11.ID3D11DeviceContext;
using ID3D11Texture2D = Vortice.Direct3D11.ID3D11Texture2D;

namespace Sakura.Framework.Graphics.Rendering.Direct3D11;

/// <summary>
/// The Direct3D 11 plane set for one video frame: three <c>R8_UNORM</c> textures (Y, U, V) under
/// <see cref="VideoPlaneLayout.Yuv420P"/>, or an <c>R8_UNORM</c> luma plus a half-resolution
/// <c>R8G8_UNORM</c> plane holding interleaved Cb and Cr under <see cref="VideoPlaneLayout.Nv12"/>
/// <remarks>
/// The video shader samples the planes (at <c>t0</c>/<c>t1</c>[/<c>t2</c>]) and does YUV -> RGB on the
/// GPU. Planes are not sRGB, the samples are raw luma/chroma, and the shader handles the
/// transfer function itself.
/// </remarks>
/// </summary>
public sealed class D3D11VideoTexture : INativeVideoTexture
{
    private readonly ID3D11Device device;
    private readonly ID3D11DeviceContext context;

    private ID3D11Texture2D yTexture;
    private ID3D11Texture2D uTexture;
    private ID3D11Texture2D vTexture;

    private ID3D11ShaderResourceView ySrv;
    private ID3D11ShaderResourceView uSrv;
    private ID3D11ShaderResourceView vSrv;

    // Clamp for normal video (avoids edge bleed), repeat for TextureFillMode.Tile. Wrap is a per-bind
    // choice, so both samplers are created once and the right one is bound in BindPlanes.
    private ID3D11SamplerState clampSampler;
    private ID3D11SamplerState repeatSampler;

    // What BindPlanes binds, one entry per plane, built once rather than every frame.
    private readonly ID3D11ShaderResourceView[] planeViews;
    private readonly ID3D11SamplerState[] clampSamplers;
    private readonly ID3D11SamplerState[] repeatSamplers;

    private readonly int chromaWidth;
    private readonly int chromaHeight;

    public int Width { get; }
    public int Height { get; }

    /// <summary>
    /// 1.5 bytes per pixel across the plane set, the same figure this texture's memory lease is taken
    /// for, so the tracker and anything displaying a size agree.
    /// </summary>
    public long PlaneBytes => NativeTextureMemory.BytesForVideoPlanes(Width, Height);

    public VideoPlaneLayout Layout { get; }

    /// <summary>
    /// Whether at least one frame has been uploaded. Written on the draw thread, read on the update
    /// thread, accessed via <see cref="Volatile"/>.
    /// </summary>
    public bool Available => Volatile.Read(ref available);
    private bool available;

    public TextureBindCounter Binds { get; } = new TextureBindCounter();

    private bool disposed;

    /// <summary>
    /// Accounts for all three plane textures in <see cref="NativeMemoryTracker"/> until they are destroyed.
    /// </summary>
    private readonly NativeMemoryLease memoryLease;

    public D3D11VideoTexture(ID3D11Device device, ID3D11DeviceContext context, int width, int height, VideoPlaneLayout layout = VideoPlaneLayout.Yuv420P)
    {
        this.device = device;
        this.context = context;
        Width = width;
        Height = height;
        Layout = layout;

        // Chroma is half-resolution (rounded up) in both layouts, matching the other backends.
        chromaWidth = (width + 1) / 2;
        chromaHeight = (height + 1) / 2;

        yTexture = createPlane(width, height, Format.R8_UNorm);
        ySrv = device.CreateShaderResourceView(yTexture);

        if (layout == VideoPlaneLayout.Nv12)
        {
            uTexture = createPlane(chromaWidth, chromaHeight, Format.R8G8_UNorm);
            uSrv = device.CreateShaderResourceView(uTexture);
        }
        else
        {
            uTexture = createPlane(chromaWidth, chromaHeight, Format.R8_UNorm);
            vTexture = createPlane(chromaWidth, chromaHeight, Format.R8_UNorm);

            uSrv = device.CreateShaderResourceView(uTexture);
            vSrv = device.CreateShaderResourceView(vTexture);
        }

        clampSampler = createSampler(TextureAddressMode.Clamp);
        repeatSampler = createSampler(TextureAddressMode.Wrap);

        planeViews = layout == VideoPlaneLayout.Nv12 ? [ySrv, uSrv] : [ySrv, uSrv, vSrv];
        clampSamplers = new ID3D11SamplerState[planeViews.Length];
        repeatSamplers = new ID3D11SamplerState[planeViews.Length];
        Array.Fill(clampSamplers, clampSampler);
        Array.Fill(repeatSamplers, repeatSampler);

        memoryLease = NativeMemoryTracker.Add(NativeMemoryCategory.Video, NativeTextureMemory.BytesForVideoPlanes(width, height));
    }

    private ID3D11Texture2D createPlane(int width, int height, Format format) =>
        device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)Math.Max(1, width),
            Height = (uint)Math.Max(1, height),
            MipLevels = 1,
            ArraySize = 1,
            Format = format,
            SampleDescription = new SampleDescription(1, 0),
            // Rewritten whole every frame from the CPU, which is what a dynamic texture is for: a discard
            // map hands over fresh memory to write straight into, where UpdateSubresource would copy
            // the frame into a staging area first and then again into the texture.
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.Write,
            MiscFlags = ResourceOptionFlags.None,
        });

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
    /// Binds the planes and their sampler to slots 0, 1 (and 2 for YUV420P) on the pixel stage,
    /// matching the video shader's samplers at <c>t0/t1[/t2]</c> and <c>s0/s1[/s2]</c>. Must be called
    /// on the draw thread. <paramref name="tiling"/> selects the repeating vs clamp sampler.
    /// </summary>
    public void BindPlanes(bool tiling)
    {
        Binds.Record();

        context.PSSetShaderResources(0, planeViews);
        context.PSSetSamplers(0, tiling ? repeatSamplers : clampSamplers);
    }

    /// <summary>
    /// Uploads a decoded frame into the planes. Must be called on the draw thread.
    /// </summary>
    public unsafe void Upload(AVFrame* frame)
    {
        int frameChromaWidth = (frame->width + 1) / 2;
        int frameChromaHeight = (frame->height + 1) / 2;

        // NV12's chroma plane holds Cb and Cr side by side, two bytes per pixel.
        int chromaBytesPerPixel = Layout == VideoPlaneLayout.Nv12 ? 2 : 1;

        uploadPlane(yTexture, frame->data[0], frame->linesize[0], Math.Min(frame->width, Width), Math.Min(frame->height, Height), 1);
        uploadPlane(uTexture, frame->data[1], frame->linesize[1], Math.Min(frameChromaWidth, chromaWidth), Math.Min(frameChromaHeight, chromaHeight), chromaBytesPerPixel);

        if (Layout == VideoPlaneLayout.Yuv420P)
            uploadPlane(vTexture, frame->data[2], frame->linesize[2], Math.Min(frameChromaWidth, chromaWidth), Math.Min(frameChromaHeight, chromaHeight), 1);

        MarkAvailable();
    }

    /// <summary>
    /// Copies one plane in row by row: FFmpeg's rows are <paramref name="linesize"/> bytes apart, and
    /// the mapped texture's are its row pitch apart, and either can carry padding past the pixels.
    /// </summary>
    private unsafe void uploadPlane(ID3D11Texture2D texture, byte* data, int linesize, int width, int height, int bytesPerPixel)
    {
        if (data == null || width <= 0 || height <= 0)
            return;

        MappedSubresource mapped = context.Map(texture, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);

        byte* destination = (byte*)mapped.DataPointer;
        long rowBytes = (long)width * bytesPerPixel;

        for (int row = 0; row < height; row++)
            Buffer.MemoryCopy(data + (long)row * linesize, destination + (long)row * mapped.RowPitch, mapped.RowPitch, rowBytes);

        context.Unmap(texture, 0);
    }

    public void MarkAvailable() => Volatile.Write(ref available, true);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        ySrv?.Dispose(); ySrv = null;
        uSrv?.Dispose(); uSrv = null;
        vSrv?.Dispose(); vSrv = null;

        yTexture?.Dispose(); yTexture = null;
        uTexture?.Dispose(); uTexture = null;
        vTexture?.Dispose(); vTexture = null;

        clampSampler?.Dispose(); clampSampler = null;
        repeatSampler?.Dispose(); repeatSampler = null;

        memoryLease?.Dispose();

        GC.SuppressFinalize(this);
    }
}
