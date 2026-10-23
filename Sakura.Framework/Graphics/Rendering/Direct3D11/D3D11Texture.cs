// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Statistic;
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

    /// <summary>
    /// Accounts for this texture's VRAM in <see cref="NativeMemoryTracker"/> until it is released.
    /// </summary>
    private readonly NativeMemoryLease memoryLease;

    /// <summary>
    /// Whether this texture has a mip chain, and whether level 0 changed since the chain was last
    /// built from it. Built on the next <see cref="Bind"/>, as GLTexture does, so a texture updated
    /// several times before it's drawn (a glyph atlas page) is only reduced once.
    /// </summary>
    private readonly bool mipmapped;

    private bool mipsDirty;

    public nint Handle => srv?.NativePointer ?? nint.Zero;
    public int Width { get; }
    public int Height { get; }
    public bool Available { get; private set; }

    public TextureBindCounter Binds { get; } = new TextureBindCounter();

    internal ID3D11ShaderResourceView ShaderResourceView => srv;
    internal ID3D11RenderTargetView RenderTargetView => rtv;

    /// <summary>
    /// Shared 1×1 white fallback, set once by <see cref="D3D11Renderer"/> at startup. Bound in place
    /// of any texture that isn't <see cref="Available"/> yet, so an un-uploaded texture samples white
    /// </summary>
    public static D3D11Texture WhitePixel { get; internal set; }

    /// <summary>
    /// Whether the device can build mip chains for the texture format, set once by
    /// <see cref="D3D11Renderer"/> at startup. Without it textures keep a single level.
    /// </summary>
    internal static bool MipGenerationSupported { get; set; }

    internal const Format TEXTURE_FORMAT = Format.R8G8B8A8_UNorm_SRgb;

    public D3D11Texture(ID3D11Device device, ID3D11DeviceContext context, int width, int height)
    {
        this.device = device;
        this.context = context;
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);

        // Mipmapped, as GL and Metal textures are, so a texture drawn smaller than its size samples the
        // level nearest its size on screen instead of skipping texels and shimmering. A 1×1 texture
        // has only the one level. Building the chain on the GPU needs the texture to be renderable.
        mipmapped = MipGenerationSupported && (Width > 1 || Height > 1);

        var desc = new Texture2DDescription
        {
            Width = (uint)Width,
            Height = (uint)Height,
            // 0 is the full chain, down to 1×1.
            MipLevels = mipmapped ? 0u : 1u,
            ArraySize = 1,
            Format = TEXTURE_FORMAT,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = mipmapped ? BindFlags.ShaderResource | BindFlags.RenderTarget : BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = mipmapped ? ResourceOptionFlags.GenerateMips : ResourceOptionFlags.None,
        };

        texture = device.CreateTexture2D(desc);
        srv = device.CreateShaderResourceView(texture);

        // Level 0 only, like GLTexture and MetalTexture, so the backends report the same figure. The
        // chain adds about a third on top.
        memoryLease = NativeTextureMemory.Lease(NativeMemoryCategory.Textures, Width, Height);
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

        memoryLease = NativeTextureMemory.Lease(NativeMemoryCategory.FrameBuffers, Width, Height);

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

        mipsDirty = mipmapped;
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

        mipsDirty = mipmapped;
        Available = true;
    }

    public void Bind(int slot = 0)
    {
        var view = srv;
        var bound = this;

        // Fall back to the white pixel when this texture hasn't been uploaded yet.
        if (!Available || view == null)
        {
            var white = WhitePixel;
            if (white == null || ReferenceEquals(white, this))
                return;
            view = white.srv;
            bound = white;
        }

        if (view == null)
            return;

        // Ahead of the draws that sample it in the same command stream, so they see the new chain.
        if (bound.mipsDirty)
        {
            context.GenerateMips(view);
            bound.mipsDirty = false;
        }

        bound.Binds.Record();
        context.PSSetShaderResource((uint)slot, view);
    }

    public void Dispose()
    {
        // Nothing can be sampled from a released texture; see MetalTexture.Dispose.
        Available = false;

        // Before the next view can recycle the COM address (see NotifyTextureDeleted).
        D3D11Renderer.NotifyTextureDeleted(srv?.NativePointer ?? nint.Zero);

        rtv?.Dispose();
        rtv = null;
        srv?.Dispose();
        srv = null;
        texture?.Dispose();
        texture = null;

        memoryLease?.Dispose();
    }
}
