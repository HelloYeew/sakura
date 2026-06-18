// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Rendering.Metal;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// A Metal-backed native texture (wraps an <c>MTLTexture</c> via the C bridge). Bring-up slice
/// whole-texture RGBA8 upload + bind to a fragment texture slot. Sub-region upload (atlas/glyph
/// blits) comes with the font/atlas slice.
/// </summary>
public sealed class MetalTexture : INativeTexture
{
    private readonly nint device; // SakuraMetalDevice*
    private nint handle;          // SakuraMetalTexture*

    public nint Handle => handle;
    public int Width { get; }
    public int Height { get; }
    public bool Available { get; private set; }

    public MetalTexture(nint device, int width, int height)
    {
        this.device = device;
        Width = width;
        Height = height;
        handle = SakuraMetalNative.sakura_metal_create_texture(device, width, height);

        if (handle == nint.Zero)
            throw new InvalidOperationException($"Failed to create Metal texture ({width}x{height}).");
    }

    private MetalTexture(nint device, nint renderTargetHandle, int width, int height)
    {
        this.device = device;
        Width = width;
        Height = height;
        handle = renderTargetHandle;

        // A render target is GPU-only and is filled by rendering into it, so it's available immediately
        // (there is no CPU upload to wait for).
        Available = true;
    }

    /// <summary>
    /// Creates a GPU-only render-target texture (renderable + sampleable, clamp-to-edge sampling) for
    /// use as an offscreen framebuffer attachment. Sampled by composite/effect passes; never uploaded.
    /// </summary>
    public static MetalTexture CreateRenderTarget(nint device, int width, int height)
    {
        nint h = SakuraMetalNative.sakura_metal_create_render_target(device, width, height);
        if (h == nint.Zero)
            throw new InvalidOperationException($"Failed to create Metal render target ({width}x{height}).");

        return new MetalTexture(device, h, width, height);
    }

    public unsafe void Upload(ReadOnlySpan<byte> data)
    {
        if (handle == nint.Zero || data.IsEmpty)
            return;

        fixed (byte* ptr = data)
            SakuraMetalNative.sakura_metal_upload_texture(handle, ptr, Width, Height);

        Available = true;
    }

    public unsafe void UploadRegion(int x, int y, int width, int height, ReadOnlySpan<byte> data)
    {
        if (handle == nint.Zero || data.IsEmpty)
            return;

        fixed (byte* ptr = data)
            SakuraMetalNative.sakura_metal_upload_texture_region(handle, x, y, width, height, ptr);

        Available = true;
    }

    public void Bind(int slot = 0)
    {
        if (handle != nint.Zero)
            SakuraMetalNative.sakura_metal_set_fragment_texture(device, handle, slot);
    }

    public void Dispose()
    {
        if (handle != nint.Zero)
        {
            SakuraMetalNative.sakura_metal_destroy_texture(handle);
            handle = nint.Zero;
        }
    }
}
