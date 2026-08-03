// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Rendering;

/// <summary>
/// A CPU-side RGBA render target, used by <see cref="HeadlessRenderer"/>'s pixel capture so tests can
/// assert what a draw path actually produces.
/// </summary>
public sealed class PixelSurface
{
    /// <summary>
    /// Width in pixels.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Height in pixels.
    /// </summary>
    public int Height { get; }

    private readonly Vector4[] pixels;

    public PixelSurface(int width, int height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        pixels = new Vector4[Width * Height];
    }

    /// <summary>
    /// The pixel at (<paramref name="x"/>, <paramref name="y"/>), with row 0 at the top.
    /// </summary>
    public Vector4 this[int x, int y]
    {
        get
        {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
                throw new ArgumentOutOfRangeException($"({x}, {y}) is outside a {Width}x{Height} surface.");

            return pixels[y * Width + x];
        }
        set
        {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
                throw new ArgumentOutOfRangeException($"({x}, {y}) is outside a {Width}x{Height} surface.");

            pixels[y * Width + x] = value;
        }
    }

    /// <summary>
    /// Overwrites every pixel with <paramref name="color"/>.
    /// </summary>
    public void Clear(Vector4 color) => Array.Fill(pixels, color);

    /// <summary>
    /// The raw backing store, row-major from the top row. Exposed for the rasterizer's inner loop.
    /// </summary>
    internal Vector4[] Pixels => pixels;
}
