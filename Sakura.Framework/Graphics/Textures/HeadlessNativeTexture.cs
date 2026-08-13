// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Graphics.Textures;

public sealed class HeadlessNativeTexture : INativeTexture
{
    private nint handle = 1;

    public nint Handle => handle;
    public int Width { get; }
    public int Height { get; }

    // There is no upload to wait for headlessly but a released texture must stop
    // reporting itself as available. Just match the real backend.
    public bool Available { get; private set; } = true;

    /// <summary>
    /// Note: it's always zero since the headless rasterizer samples <see cref="Surface"/> straight from the CPU and never
    /// binds anything, so there is nothing to count.
    /// </summary>
    public TextureBindCounter Binds { get; } = new TextureBindCounter();

    /// <summary>
    /// CPU pixels backing this texture when <see cref="Rendering.HeadlessRenderer"/> pixel capture is on,
    /// otherwise null. Only framebuffer color attachments carry one; a texture without a surface samples
    /// as opaque white, which is enough for the compositing questions capture exists to answer.
    /// </summary>
    internal Rendering.PixelSurface? Surface { get; set; }

    public HeadlessNativeTexture(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public void Upload(ReadOnlySpan<byte> data) { }
    public void UploadRegion(int x, int y, int width, int height, ReadOnlySpan<byte> data) { }
    public void Bind(int slot = 0) { }

    public void Dispose()
    {
        handle = 0;
        Available = false;
    }
}
