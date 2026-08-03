// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Maths;
using SakuraVertex = Sakura.Framework.Graphics.Rendering.Vertex.Vertex;

namespace Sakura.Framework.Graphics.Rendering;

/// <summary>
/// A small software rasterizer backing <see cref="HeadlessRenderer"/>'s pixel capture. It exists so that
/// questions about compositing which blend mode a draw path selects, and how two passes combine.
/// Can be asserted by a normal unit test instead of eyeballed in the visual browser.
/// </summary>
internal sealed class HeadlessRasterizer
{
    /// <summary>
    /// Samples a texture surface. Texture coordinates follow the framework's framebuffer convention:
    /// row 0 of a surface is at the <em>bottom</em> of texture space, which is why
    /// <c>BufferedContainerDrawNode.fillQuad</c> emits V-flipped coordinates. Mirroring the flip here is
    /// what makes a round trip through a framebuffer an identity in headless capture, as it is on a GPU.
    /// </summary>
    private static Vector4 sample(PixelSurface surface, Vector2 uv)
    {
        // Nearest-neighbor, not bilinear: a filtered sample would blur exact expected values across
        // neighboring pixels and make assertions depend on the filter rather than on the blend.
        int x = (int)MathF.Floor(uv.X * surface.Width);
        int y = (int)MathF.Floor((1f - uv.Y) * surface.Height);

        x = Math.Clamp(x, 0, surface.Width - 1);
        y = Math.Clamp(y, 0, surface.Height - 1);

        return surface[x, y];
    }

    /// <summary>
    /// Applies one blend mode. <paramref name="src"/> is the shaded fragment (texture color times vertex
    /// color, as <c>shader.frag</c> computes it) and <paramref name="dst"/> the current target pixel.
    /// </summary>
    private static Vector4 blend(BlendingMode mode, Vector4 src, Vector4 dst)
    {
        // Every mode blends alpha separately from RGB, matching BlendFuncSeparate in the backends: a
        // single factor pair would apply the RGB source factor to alpha too, which produces wrong
        // accumulated coverage when rendering into a transparent offscreen target.
        float srcRgb, dstRgb, srcA, dstA;

        switch (mode)
        {
            // Additive: (SrcAlpha, One), alpha (One, One).
            case BlendingMode.Additive:
                srcRgb = src.W;
                dstRgb = 1f;
                srcA = 1f;
                dstA = 1f;
                break;

            // Opaque: (One, Zero), alpha (One, Zero).
            case BlendingMode.Opaque:
                srcRgb = 1f;
                dstRgb = 0f;
                srcA = 1f;
                dstA = 0f;
                break;

            // Screen: (One, OneMinusSrcColor), alpha (One, OneMinusSrcAlpha). The RGB destination factor
            // is per-channel, so it is handled out of band below.
            case BlendingMode.Screen:
                return new Vector4(
                    src.X + dst.X * (1f - src.X),
                    src.Y + dst.Y * (1f - src.Y),
                    src.Z + dst.Z * (1f - src.Z),
                    src.W + dst.W * (1f - src.W));

            // Multiply: (DstColor, OneMinusSrcAlpha), alpha (One, OneMinusSrcAlpha). Also per-channel.
            case BlendingMode.Multiply:
                return new Vector4(
                    src.X * dst.X + dst.X * (1f - src.W),
                    src.Y * dst.Y + dst.Y * (1f - src.W),
                    src.Z * dst.Z + dst.Z * (1f - src.W),
                    src.W + dst.W * (1f - src.W));

            // Premultiplied: (One, OneMinusSrcAlpha), alpha (One, OneMinusSrcAlpha). For source RGB that
            // already carries its own alpha — which is what a framebuffer's contents are.
            case BlendingMode.Premultiplied:
                srcRgb = 1f;
                dstRgb = 1f - src.W;
                srcA = 1f;
                dstA = 1f - src.W;
                break;

            // Alpha: (SrcAlpha, OneMinusSrcAlpha), alpha (One, OneMinusSrcAlpha).
            case BlendingMode.Alpha:
            default:
                srcRgb = src.W;
                dstRgb = 1f - src.W;
                srcA = 1f;
                dstA = 1f - src.W;
                break;
        }

        return new Vector4(
            src.X * srcRgb + dst.X * dstRgb,
            src.Y * srcRgb + dst.Y * dstRgb,
            src.Z * srcRgb + dst.Z * dstRgb,
            src.W * srcA + dst.W * dstA);
    }

    /// <summary>
    /// Rasterize one quad into <paramref name="target"/>, triangulated (0,1,2) then (2,3,0) exactly as
    /// <c>TriangleBatch.AddQuad</c> does.
    /// </summary>
    /// <param name="target">The surface being rendered into.</param>
    /// <param name="quad">Four vertices in screen space.</param>
    /// <param name="texture">The texture surface, or null to sample opaque white.</param>
    /// <param name="mode">The blend mode in force.</param>
    /// <param name="viewport">
    /// Maps screen space onto <paramref name="target"/>. Null when drawing to the screen surface, where the
    /// mapping is the identity.
    /// </param>
    public void DrawQuad(PixelSurface target, ReadOnlySpan<SakuraVertex> quad, PixelSurface? texture, BlendingMode mode, ViewportTransform? viewport)
    {
        if (quad.Length < 4)
            return;

        Span<SakuraVertex> mapped = stackalloc SakuraVertex[4];

        for (int i = 0; i < 4; i++)
        {
            mapped[i] = quad[i];

            if (viewport is { } v)
                mapped[i].Position = v.Apply(quad[i].Position);
        }

        rasterizeTriangle(target, mapped[0], mapped[1], mapped[2], texture, mode);
        rasterizeTriangle(target, mapped[2], mapped[3], mapped[0], texture, mode);
    }

    private static void rasterizeTriangle(PixelSurface target, SakuraVertex a, SakuraVertex b, SakuraVertex c, PixelSurface? texture, BlendingMode mode)
    {
        float area = edge(a.Position, b.Position, c.Position);

        // Degenerate triangle (zero-size or zero-thickness quad): nothing to fill.
        if (MathF.Abs(area) < 1e-6f)
            return;

        // Normalise winding so the fill rule below has one orientation to reason about. Swapping two
        // vertices flips the sign of every edge function together with the area.
        if (area < 0)
        {
            (b, c) = (c, b);
            area = -area;
        }

        int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(a.Position.X, MathF.Min(b.Position.X, c.Position.X))));
        int maxX = Math.Min(target.Width - 1, (int)MathF.Ceiling(MathF.Max(a.Position.X, MathF.Max(b.Position.X, c.Position.X))));
        int minY = Math.Max(0, (int)MathF.Floor(MathF.Min(a.Position.Y, MathF.Min(b.Position.Y, c.Position.Y))));
        int maxY = Math.Min(target.Height - 1, (int)MathF.Ceiling(MathF.Max(a.Position.Y, MathF.Max(b.Position.Y, c.Position.Y))));

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                // Pixel centres, so a quad spanning exactly [0, n) covers n pixels rather than n + 1.
                var p = new Vector2(x + 0.5f, y + 0.5f);

                float e0 = edge(b.Position, c.Position, p);
                float e1 = edge(c.Position, a.Position, p);
                float e2 = edge(a.Position, b.Position, p);

                // No coverage weighting: a fragment is either in or out. See the class remarks — this
                // does not model antialiasing, and tests should assert interior pixels rather than edges.
                if (!covers(e0, b.Position, c.Position) || !covers(e1, c.Position, a.Position) || !covers(e2, a.Position, b.Position))
                    continue;

                float w0 = e0 / area;
                float w1 = e1 / area;
                float w2 = e2 / area;

                var color = new Vector4(
                    a.Color.X * w0 + b.Color.X * w1 + c.Color.X * w2,
                    a.Color.Y * w0 + b.Color.Y * w1 + c.Color.Y * w2,
                    a.Color.Z * w0 + b.Color.Z * w1 + c.Color.Z * w2,
                    a.Color.W * w0 + b.Color.W * w1 + c.Color.W * w2);

                if (texture != null)
                {
                    var uv = new Vector2(
                        a.TexCoords.X * w0 + b.TexCoords.X * w1 + c.TexCoords.X * w2,
                        a.TexCoords.Y * w0 + b.TexCoords.Y * w1 + c.TexCoords.Y * w2);

                    var texel = sample(texture, uv);

                    // shader.frag: texColor *= v_Color, then straight out. No un-premultiply anywhere,
                    // which is the whole reason the composite blend mode matters.
                    color = new Vector4(color.X * texel.X, color.Y * texel.Y, color.Z * texel.Z, color.W * texel.W);
                }

                target[x, y] = blend(mode, color, target[x, y]);
            }
        }
    }

    private static float edge(Vector2 a, Vector2 b, Vector2 p)
        => (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);

    /// <summary>
    /// The top-left fill rule, for a triangle already normalised to positive area in y-down screen space:
    /// strictly-inside fragments always count, and a fragment lying exactly <em>on</em> an edge counts only
    /// for top and left edges.
    /// </summary>
    /// <remarks>
    /// Without this the two triangles of a quad both claim the pixels on their shared diagonal, so those
    /// pixels get blended twice — which for a translucent quad is not a faint seam but a visibly different
    /// color, and it silently corrupted the first version of the compositing tests. A GPU applies the same
    /// rule for the same reason.
    /// </remarks>
    private static bool covers(float edgeValue, Vector2 from, Vector2 to)
    {
        if (edgeValue > 0f)
            return true;

        if (edgeValue < 0f)
            return false;

        float dx = to.X - from.X;
        float dy = to.Y - from.Y;

        // dy < 0 is a left edge (descending in a y-down space); dy == 0 with dx > 0 is a top edge.
        return dy < 0f || (dy == 0f && dx > 0f);
    }

    /// <summary>
    /// Maps screen space onto a bound framebuffer, standing in for the projection the real backends bind:
    /// the source rect is stretched to cover the whole target.
    /// </summary>
    internal readonly struct ViewportTransform
    {
        private readonly RectangleF source;
        private readonly int width;
        private readonly int height;

        public ViewportTransform(RectangleF source, int width, int height)
        {
            this.source = source;
            this.width = width;
            this.height = height;
        }

        public Vector2 Apply(Vector2 position)
        {
            float x = source.Width > 0 ? (position.X - source.X) / source.Width * width : 0f;
            float y = source.Height > 0 ? (position.Y - source.Y) / source.Height * height : 0f;

            return new Vector2(x, y);
        }
    }
}
