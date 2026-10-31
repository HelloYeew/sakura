// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Vertex;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Drawables;

/// <summary>
/// A graph's marks, all drawn at once: dots, rings, lines (solid or dashed), areas filled down to a baseline, boxes and
/// triangles, each in its own color, at points in its own units (its size, in pixels before scaling). Whatever plots on
/// it clears it and adds its marks again when they change; a thousand dots are one draw, not a thousand drawables. Neither
/// a fade (<see cref="Drawable.UpdateVertexAlpha"/>) nor a parent's layout that doesn't move it
/// (<see cref="Drawable.KeepVertices"/>) builds them again.
/// </summary>
/// <remarks>
/// Dots, rings and lines are smooth at their edges (fading over a screen pixel, as masks do); areas, boxes and triangles
/// are for fills, and aren't. It fills its parent by default. <see cref="Drawable.Color"/> only adds its alpha: each mark
/// has its own color.
/// </remarks>
public partial class PlotCanvas : Drawable
{
    private enum Kind
    {
        Dot,
        Ring,
        Line,
        Area,
        Box,
        Triangle
    }

    // A mark: its kind and color, where its points start in the shared list and how many it has, and its sizes (a dot's
    // or ring's radius, a ring's or line's thickness, an area's baseline, a dash and its gap).
    private readonly record struct Mark(Kind Kind, Color Color, int Start, int Count, float A = 0, float B = 0, float Dash = 0, float Gap = 0);

    private readonly List<Mark> marks = new List<Mark>();
    private readonly List<Vector2> points = new List<Vector2>();

    /// <summary>
    /// How many marks it's drawing.
    /// </summary>
    public int Count => marks.Count;

    public PlotCanvas()
    {
        RelativeSizeAxes = Axes.Both;
    }

    public override bool HandlePositionalInput => false;

    public void Clear()
    {
        if (marks.Count == 0)
            return;

        marks.Clear();
        points.Clear();
        marksChanged();
    }

    public void Dot(Vector2 centre, float radius, Color color) => add(Kind.Dot, color, centre, a: radius);

    public void Ring(Vector2 centre, float radius, float thickness, Color color) => add(Kind.Ring, color, centre, a: radius, b: thickness);

    public void Box(RectangleF box, Color color) => add(Kind.Box, color, new Vector2(box.X, box.Y), new Vector2(box.X + box.Width, box.Y + box.Height));

    public void Triangle(Vector2 a, Vector2 b, Vector2 c, Color color) => add(Kind.Triangle, color, a, b, c);

    /// <summary>
    /// A line through <paramref name="through"/>, dashed when <paramref name="dash"/> is given (dashes that long, gaps
    /// <paramref name="gap"/> long, along it).
    /// </summary>
    public void Line(IReadOnlyList<Vector2> through, float thickness, Color color, float dash = 0, float gap = 0)
    {
        if (through.Count < 2)
            return;

        marks.Add(new Mark(Kind.Line, color, points.Count, through.Count, thickness, 0, dash, gap));
        points.AddRange(through);
        marksChanged();
    }

    public void Line(Vector2 from, Vector2 to, float thickness, Color color, float dash = 0, float gap = 0) => Line(new[] { from, to }, thickness, color, dash, gap);

    /// <summary>
    /// The area between a line through <paramref name="top"/> (left to right) and <paramref name="baseline"/> under it.
    /// </summary>
    public void Area(IReadOnlyList<Vector2> top, float baseline, Color color)
    {
        if (top.Count < 2)
            return;

        marks.Add(new Mark(Kind.Area, color, points.Count, top.Count, baseline));
        points.AddRange(top);
        marksChanged();
    }

    private void add(Kind kind, Color color, Vector2 p0, float a = 0, float b = 0)
    {
        marks.Add(new Mark(kind, color, points.Count, 1, a, b));
        points.Add(p0);
        marksChanged();
    }

    private void add(Kind kind, Color color, params Vector2[] at)
    {
        marks.Add(new Mark(kind, color, points.Count, at.Length));
        points.AddRange(at);
        marksChanged();
    }

    // Its vertices have to be built again; its size, which a parent would care about, hasn't changed.
    private void marksChanged()
    {
        marksBuilt = false;
        Invalidate(InvalidationFlags.DrawInfo, false);
    }

    protected internal override VertexTopology Topology => VertexTopology.Triangles;

    protected override void UpdateDrawColor() => UpdateVertexAlpha();

    private bool marksBuilt;
    private Vector2 size;
    private float pixel;
    private float minX, minY, maxX, maxY;

    protected override void GenerateVertices()
    {
        // A parent's layout changing without moving it (a mask revealing it, say) brings it here with nothing to do.
        if (marksBuilt && KeepVertices())
            return;

        marksBuilt = true;
        size = AppliedDrawSize;

        if (marks.Count == 0 || size.X <= 0 || size.Y <= 0)
        {
            SetVertexCount(0);
            DrawRectangle = new RectangleF(0, 0, 0, 0);
            return;
        }

        // Screen pixels to one of its own.
        pixel = Vector2.Distance(toScreen(Vector2.Zero), toScreen(Vector2.UnitX));

        int count = 0;
        foreach (Mark mark in marks)
            count += verticesFor(mark);

        SetVertexCount(count);

        minX = minY = float.MaxValue;
        maxX = maxY = float.MinValue;
        int offset = 0;

        foreach (Mark mark in marks)
        {
            Vector4 color = linear(mark.Color);

            offset = mark.Kind switch
            {
                Kind.Dot => writeDot(offset, points[mark.Start], mark.A, color),
                Kind.Ring => writeRing(offset, points[mark.Start], mark.A, mark.B, color),
                Kind.Line => writeLine(offset, mark, color),
                Kind.Area => writeArea(offset, mark, color),
                Kind.Box => writeBox(offset, points[mark.Start], points[mark.Start + 1], color),
                _ => writeTriangle(offset, points[mark.Start], points[mark.Start + 1], points[mark.Start + 2], color)
            };
        }

        // Anything left unwritten (a dash pattern ending short) draws nothing.
        for (; offset < count; offset++)
            Vertices[offset] = default;

        DrawRectangle = minX <= maxX ? new RectangleF(minX, minY, maxX - minX, maxY - minY) : new RectangleF(0, 0, 0, 0);
    }

    private int verticesFor(Mark mark) => mark.Kind switch
    {
        Kind.Dot => 9 * segmentsFor(mark.A),
        Kind.Ring => FeatheredStrip.VerticesPerPiece(true) * segmentsFor(mark.A),
        Kind.Line => FeatheredStrip.VerticesPerPiece(true) * lineSegments(mark),
        Kind.Area => 6 * (mark.Count - 1),
        Kind.Box => 6,
        _ => 3
    };

    // Rounder the bigger it's drawn.
    private int segmentsFor(float radius) => Math.Clamp((int)MathF.Ceiling(radius * pixel * 2.5f), 8, 48);

    // How many pieces a line is drawn in: one a stretch, or for a dashed one, as many as its dashes (at most).
    private int lineSegments(Mark mark)
    {
        if (mark.Dash <= 0)
            return mark.Count - 1;

        float length = 0;
        for (int i = mark.Start + 1; i < mark.Start + mark.Count; i++)
            length += Vector2.Distance(points[i - 1], points[i]);

        // A dash can cross a corner, which splits it in two.
        return (int)MathF.Ceiling(length / (mark.Dash + mark.Gap)) + mark.Count;
    }

    private int writeDot(int offset, Vector2 centre, float radius, Vector4 color)
    {
        int segments = segmentsFor(radius);
        float feather = FeatheredStrip.EDGE_FEATHER;
        float screenRadius = radius * pixel;
        float inner = Math.Max(0, screenRadius - feather) / pixel;
        float outer = (screenRadius + feather) / pixel;
        Vector4 clear = color with { W = 0 };
        Vector4 solid = color with { W = color.W * Math.Min(1, screenRadius / feather) };

        Vector2 middle = toScreen(centre);

        for (int i = 0; i < segments; i++)
        {
            Vector2 from = around(i, segments), to = around(i + 1, segments);
            Vector2 innerFrom = toScreen(centre + from * inner), innerTo = toScreen(centre + to * inner);
            Vector2 outerFrom = toScreen(centre + from * outer), outerTo = toScreen(centre + to * outer);

            offset = vertex(offset, middle, solid);
            offset = vertex(offset, innerFrom, solid);
            offset = vertex(offset, innerTo, solid);

            offset = vertex(offset, innerFrom, solid);
            offset = vertex(offset, outerFrom, clear);
            offset = vertex(offset, outerTo, clear);
            offset = vertex(offset, outerTo, clear);
            offset = vertex(offset, innerTo, solid);
            offset = vertex(offset, innerFrom, solid);
        }

        return offset;
    }

    private int writeRing(int offset, Vector2 centre, float radius, float thickness, Vector4 color)
    {
        int segments = segmentsFor(radius);
        StripSection previous = default;

        for (int i = 0; i <= segments; i++)
        {
            Vector2 direction = around(i, segments);
            var section = new StripSection(toScreen(centre + direction * (radius - thickness / 2)), toScreen(centre + direction * (radius + thickness / 2)), FeatheredStrip.EDGE_FEATHER);
            grow(section);

            if (i > 0)
                offset = FeatheredStrip.Write(Vertices, offset, previous, section, color, true);

            previous = section;
        }

        return offset;
    }

    private int writeLine(int offset, Mark mark, Vector4 color)
    {
        float half = mark.A / 2;

        if (mark.Dash <= 0)
        {
            for (int i = mark.Start + 1; i < mark.Start + mark.Count; i++)
                offset = writeSegment(offset, points[i - 1], points[i], half, color);

            return offset;
        }

        // Along the line, dash on and gap off, the pattern carrying on round its corners.
        float period = mark.Dash + mark.Gap;
        float along = 0;
        int limit = offset + FeatheredStrip.VerticesPerPiece(true) * lineSegments(mark);

        for (int i = mark.Start + 1; i < mark.Start + mark.Count; i++)
        {
            Vector2 from = points[i - 1], to = points[i];
            float length = Vector2.Distance(from, to);

            if (length <= 0)
                continue;

            float at = 0;

            while (at < length && offset < limit)
            {
                float phase = along % period;
                float left = phase < mark.Dash ? mark.Dash - phase : period - phase;
                float step = Math.Min(left, length - at);

                if (phase < mark.Dash)
                    offset = writeSegment(offset, Vector2.Lerp(from, to, at / length), Vector2.Lerp(from, to, (at + step) / length), half, color);

                at += step;
                along += step;
            }
        }

        return offset;
    }

    private int writeSegment(int offset, Vector2 from, Vector2 to, float half, Vector4 color)
    {
        Vector2 along = to - from;
        float length = along.Length();

        if (length <= 0)
            return offset;

        Vector2 across = new Vector2(-along.Y, along.X) / length * half;
        var start = new StripSection(toScreen(from - across), toScreen(from + across), FeatheredStrip.EDGE_FEATHER);
        var end = new StripSection(toScreen(to - across), toScreen(to + across), FeatheredStrip.EDGE_FEATHER);
        grow(start);
        grow(end);

        return FeatheredStrip.Write(Vertices, offset, start, end, color, true);
    }

    private int writeArea(int offset, Mark mark, Vector4 color)
    {
        float baseline = mark.A;

        for (int i = mark.Start + 1; i < mark.Start + mark.Count; i++)
        {
            Vector2 a = points[i - 1], b = points[i];
            offset = quad(offset, toScreen(a), toScreen(b), toScreen(new Vector2(b.X, baseline)), toScreen(new Vector2(a.X, baseline)), color);
        }

        return offset;
    }

    private int writeBox(int offset, Vector2 topLeft, Vector2 bottomRight, Vector4 color) =>
        quad(offset, toScreen(topLeft), toScreen(new Vector2(bottomRight.X, topLeft.Y)), toScreen(bottomRight), toScreen(new Vector2(topLeft.X, bottomRight.Y)), color);

    private int writeTriangle(int offset, Vector2 a, Vector2 b, Vector2 c, Vector4 color)
    {
        offset = vertex(offset, toScreen(a), color);
        offset = vertex(offset, toScreen(b), color);
        return vertex(offset, toScreen(c), color);
    }

    private int quad(int offset, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, Vector4 color)
    {
        offset = vertex(offset, p0, color);
        offset = vertex(offset, p1, color);
        offset = vertex(offset, p2, color);
        offset = vertex(offset, p2, color);
        offset = vertex(offset, p3, color);
        return vertex(offset, p0, color);
    }

    private int vertex(int offset, Vector2 position, Vector4 color)
    {
        Vertices[offset] = new Vertex { Position = position, TexCoords = Vector2.Zero, Color = color };
        grow(position);
        return offset + 1;
    }

    private void grow(in StripSection section)
    {
        grow(section.AOuter);
        grow(section.BOuter);
    }

    private void grow(Vector2 at)
    {
        minX = Math.Min(minX, at.X);
        minY = Math.Min(minY, at.Y);
        maxX = Math.Max(maxX, at.X);
        maxY = Math.Max(maxY, at.Y);
    }

    private static Vector2 around(int i, int segments)
    {
        float angle = MathF.Tau * i / segments;
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
    }

    private Vector4 linear(Color color) => new Vector4(
        ColorExtensions.SrgbToLinear(color.R),
        ColorExtensions.SrgbToLinear(color.G),
        ColorExtensions.SrgbToLinear(color.B),
        DrawAlpha * (color.A / 255f) * (Color.A / 255f));

    private Vector2 toScreen(Vector2 local) => Vector2.Transform(new Vector2(local.X / size.X, local.Y / size.Y), ModelMatrix);
}
