// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Rendering.Vertex;

/// <summary>
/// One cross-section of a band drawn as triangles (a ring, a branch): from edge <see cref="A"/> to edge <see cref="B"/>,
/// in screen space, with the points its feathered edges fade between.
/// </summary>
public readonly struct StripSection
{
    public readonly Vector2 A;
    public readonly Vector2 B;

    // The edges pushed out and pulled in by the feather: the band fades from clear outside to full inside.
    public readonly Vector2 AOuter;
    public readonly Vector2 AInner;
    public readonly Vector2 BInner;
    public readonly Vector2 BOuter;

    /// <summary>
    /// How solid the band's middle is: under 1 where it's thinner than its feather, which dims it rather than drawing
    /// it wider than it is.
    /// </summary>
    public readonly float Alpha;

    /// <param name="a">One edge, in screen space.</param>
    /// <param name="b">The other.</param>
    /// <param name="feather">How far each edge fades either side of where it is, in screen pixels.</param>
    public StripSection(Vector2 a, Vector2 b, float feather)
    {
        A = a;
        B = b;

        Vector2 across = b - a;
        float width = across.Length();
        Vector2 direction = width > 0 ? across / width : Vector2.Zero;

        AOuter = a - direction * feather;
        BOuter = b + direction * feather;

        if (width >= 2 * feather)
        {
            AInner = a + direction * feather;
            BInner = b - direction * feather;
            Alpha = 1;
        }
        else
        {
            AInner = BInner = (a + b) / 2;
            Alpha = feather > 0 ? width / (2 * feather) : 1;
        }
    }
}

/// <summary>
/// Writes bands as triangles between <see cref="StripSection"/>s. Smooth, a band's edges fade over a screen pixel (as
/// masks draw theirs) instead of stepping; that takes three quads a piece instead of one.
/// </summary>
/// <remarks>
/// It's for a drawable's <see cref="Drawables.Drawable.GenerateVertices"/>, with a triangle topology.
/// <see cref="Drawables.RingArc"/> and <see cref="Drawables.PlotCanvas"/> are built on it.
/// </remarks>
public static class FeatheredStrip
{
    /// <summary>
    /// How far a smooth edge fades either side of where it is, in screen pixels.
    /// </summary>
    public const float EDGE_FEATHER = 0.5f;

    /// <summary>
    /// The vertices one piece takes.
    /// </summary>
    public static int VerticesPerPiece(bool smooth) => smooth ? 18 : 6;

    /// <summary>
    /// Writes the piece between two sections at <paramref name="offset"/>.
    /// </summary>
    /// <returns>The offset after it.</returns>
    public static int Write(Span<Vertex> vertices, int offset, in StripSection from, in StripSection to, Vector4 color, bool smooth) =>
        Write(vertices, offset, from, to, color, color, smooth);

    /// <summary>
    /// Writes the piece between two sections at <paramref name="offset"/>, its color going from one to the other along it.
    /// </summary>
    /// <returns>The offset after it.</returns>
    public static int Write(Span<Vertex> vertices, int offset, in StripSection from, in StripSection to, Vector4 fromSolid, Vector4 toSolid, bool smooth)
    {
        if (!smooth)
            return quad(vertices, offset, from.A, from.B, to.B, to.A, fromSolid, fromSolid, toSolid, toSolid);

        Vector4 fromColor = fromSolid with { W = fromSolid.W * from.Alpha };
        Vector4 toColor = toSolid with { W = toSolid.W * to.Alpha };
        Vector4 fromClear = fromSolid with { W = 0 };
        Vector4 toClear = toSolid with { W = 0 };

        offset = quad(vertices, offset, from.AOuter, from.AInner, to.AInner, to.AOuter, fromClear, fromColor, toColor, toClear);
        offset = quad(vertices, offset, from.AInner, from.BInner, to.BInner, to.AInner, fromColor, fromColor, toColor, toColor);
        return quad(vertices, offset, from.BInner, from.BOuter, to.BOuter, to.BInner, fromColor, fromClear, toClear, toColor);
    }

    // Two triangles: p0 p1 p2 and p2 p3 p0.
    private static int quad(Span<Vertex> vertices, int offset, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, Vector4 c0, Vector4 c1, Vector4 c2, Vector4 c3)
    {
        vertices[offset + 0] = new Vertex { Position = p0, TexCoords = new Vector2(0, 0), Color = c0 };
        vertices[offset + 1] = new Vertex { Position = p1, TexCoords = new Vector2(1, 0), Color = c1 };
        vertices[offset + 2] = new Vertex { Position = p2, TexCoords = new Vector2(1, 1), Color = c2 };
        vertices[offset + 3] = new Vertex { Position = p2, TexCoords = new Vector2(1, 1), Color = c2 };
        vertices[offset + 4] = new Vertex { Position = p3, TexCoords = new Vector2(0, 1), Color = c3 };
        vertices[offset + 5] = new Vertex { Position = p0, TexCoords = new Vector2(0, 0), Color = c0 };
        return offset + 6;
    }
}
