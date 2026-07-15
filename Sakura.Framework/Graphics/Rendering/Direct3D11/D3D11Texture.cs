// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Textures;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Sakura.Framework.Graphics.Rendering.Direct3D11;

/// <summary>
/// A Direct3D 11-backed native texture
/// </summary>
public sealed class D3D11Texture : INativeTexture
{
    private readonly ID3D11Device device;
    private readonly ID3D11DeviceContext context;

    private ID3D11Texture2D texture;
    private ID3D11ShaderResourceView srv;
    private ID3D11RenderTargetView rtv;

    public nint Handle => srv?.NativePointer ?? nint.Zero;
    public int Width { get; }
    public int Height { get; }
    public bool Available { get; private set; }

    internal ID3D11ShaderResourceView ShaderResourceView => srv;
    internal ID3D11RenderTargetView RenderTargetView => rtv;

    /// <summary>
    /// Shared 1×1 white fallback, set once by <see cref="D3D11Renderer"/> at startup. Bound in place
    /// of any texture that isn't <see cref="Available"/> yet, so an un-uploaded texture samples white
    /// </summary>
    public static D3D11Texture WhitePixel { get; internal set; }

    public D3D11Texture(ID3D11Device device, ID3D11DeviceContext context, int width, int height)
    {
        this.device = device;
        this.context = context;
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);

        var desc = new Texture2DDescription
        {
            Width = (uint)Width,
            Height = (uint)Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.R8G8B8A8_UNorm_SRgb,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None,
        };

        texture = device.CreateTexture2D(desc);
        srv = device.CreateShaderResourceView(texture);
    }

    private D3D11Texture(ID3D11Device device, ID3D11DeviceContext context, int width, int height, bool renderTarget)
    {
        this.device = device;
        this.context = context;
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);

        var desc = new Texture2DDescription
        {
            Width = (uint)Width,
            Height = (uint)Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm_SRgb,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None,
        };

        texture = device.CreateTexture2D(desc);
        srv = device.CreateShaderResourceView(texture);
        rtv = device.CreateRenderTargetView(texture);

        // GPU-only: filled by rendering into it, so it's available immediately (no CPU upload).
        Available = true;
    }

    /// <summary>
    /// Creates a GPU-only render-target texture (renderable + sampleable) for use as an offscreen
    /// framebuffer attachment. Mirrors <see cref="MetalTexture.CreateRenderTarget"/>.
    /// </summary>
    public static D3D11Texture CreateRenderTarget(ID3D11Device device, ID3D11DeviceContext context, int width, int height) =>
        new D3D11Texture(device, context, width, height, renderTarget: true);

    public unsafe void Upload(ReadOnlySpan<byte> data)
    {
        if (texture == null || data.IsEmpty)
            return;

        fixed (byte* ptr = data)
        {
            context.UpdateSubresource(texture, 0, (Box?)null, (nint)ptr, (uint)(Width * 4), 0);
        }

        Available = true;
    }

    public unsafe void UploadRegion(int x, int y, int width, int height, ReadOnlySpan<byte> data)
    {
        if (texture == null || data.IsEmpty)
            return;

        var box = new Box(x, y, 0, x + width, y + height, 1);

        fixed (byte* ptr = data)
        {
            // Source data covers just the region: row pitch = region width * 4.
            context.UpdateSubresource(texture, 0, (Box?)box, (nint)ptr, (uint)(width * 4), 0);
        }

        Available = true;
    }

    public void Bind(int slot = 0)
    {
        var view = srv;

        // Fall back to the white pixel when this texture hasn't been uploaded yet.
        if (!Available || view == null)
        {
            var white = WhitePixel;
            if (white == null || ReferenceEquals(white, this))
                return;
            view = white.srv;
        }

        if (view != null)
            context.PSSetShaderResource((uint)slot, view);
    }

    public void Dispose()
    {
        rtv?.Dispose();
        rtv = null;
        srv?.Dispose();
        srv = null;
        texture?.Dispose();
        texture = null;
    }
}
