// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Threading;
using FFmpeg.AutoGen;
using Sakura.Framework.Graphics.Textures;
using Vortice.Direct3D11;
using Vortice.DXGI;

// FFmpeg.AutoGen also declares ID3D11* interop types; alias the D3D ones to Vortice's.
using ID3D11Device = Vortice.Direct3D11.ID3D11Device;
using ID3D11DeviceContext = Vortice.Direct3D11.ID3D11DeviceContext;
using ID3D11Texture2D = Vortice.Direct3D11.ID3D11Texture2D;

namespace Sakura.Framework.Graphics.Rendering.Direct3D11;

/// <summary>
/// Three single-channel <c>R8_UNORM</c> Direct3D 11 textures holding the Y, U, V planes of a YUV420P
/// frame
/// <remarks>
/// The video shader samples all three planes (at <c>t0</c>/<c>t1</c>/<c>t2</c>) and does YUV -> RGB on the
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

    public int Width { get; }
    public int Height { get; }

    /// <summary>
    /// Whether at least one frame has been uploaded. Written on the draw thread, read on the update
    /// thread, accessed via <see cref="Volatile"/>.
    /// </summary>
    public bool Available => Volatile.Read(ref available);
    private bool available;

    private bool disposed;

    public D3D11VideoTexture(ID3D11Device device, ID3D11DeviceContext context, int width, int height)
    {
        this.device = device;
        this.context = context;
        Width = width;
        Height = height;

        // YUV420P: chroma planes are half-resolution (rounded up), matching the other backends.
        int chromaWidth = (width + 1) / 2;
        int chromaHeight = (height + 1) / 2;

        yTexture = createPlane(width, height);
        uTexture = createPlane(chromaWidth, chromaHeight);
        vTexture = createPlane(chromaWidth, chromaHeight);

        ySrv = device.CreateShaderResourceView(yTexture);
        uSrv = device.CreateShaderResourceView(uTexture);
        vSrv = device.CreateShaderResourceView(vTexture);

        clampSampler = createSampler(TextureAddressMode.Clamp);
        repeatSampler = createSampler(TextureAddressMode.Wrap);
    }

    private ID3D11Texture2D createPlane(int width, int height) =>
        device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)Math.Max(1, width),
            Height = (uint)Math.Max(1, height),
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.R8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
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
    /// Binds the Y, U, V planes and their sampler to slots 0, 1, 2 on the pixel stage (matching the
    /// video shader's <c>u_TextureY/U/V</c> at <c>t0/t1/t2</c> and <c>s0/s1/s2</c>). Must be called on
    /// the draw thread. <paramref name="tiling"/> selects the repeating vs clamp sampler.
    /// </summary>
    public void BindPlanes(bool tiling)
    {
        var sampler = tiling ? repeatSampler : clampSampler;

        context.PSSetShaderResources(0, new[] { ySrv, uSrv, vSrv });
        context.PSSetSamplers(0, new[] { sampler, sampler, sampler });
    }

    /// <summary>
    /// Uploads a decoded YUV420P frame into the Y, U, V planes. Each plane's FFmpeg linesize (which may
    /// exceed the plane width due to row padding) is passed as the source row pitch, so
    /// <c>UpdateSubresource</c> skips the padding. Must be called on the draw thread.
    /// </summary>
    public unsafe void Upload(AVFrame* frame)
    {
        int width = frame->width;
        int height = frame->height;
        int chromaWidth = (width + 1) / 2;
        int chromaHeight = (height + 1) / 2;

        uploadPlane(yTexture, frame->data[0], frame->linesize[0], width, height);
        uploadPlane(uTexture, frame->data[1], frame->linesize[1], chromaWidth, chromaHeight);
        uploadPlane(vTexture, frame->data[2], frame->linesize[2], chromaWidth, chromaHeight);

        MarkAvailable();
    }

    private unsafe void uploadPlane(ID3D11Texture2D texture, byte* data, int linesize, int width, int height)
    {
        if (data == null || width <= 0 || height <= 0)
            return;

        // R8 (1 byte/texel): the source row pitch is the FFmpeg linesize; UpdateSubresource reads the
        // plane width per row and advances `linesize` bytes, so any trailing row padding is skipped.
        context.UpdateSubresource(texture, 0, null, (nint)data, (uint)linesize, 0);
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

        GC.SuppressFinalize(this);
    }
}
