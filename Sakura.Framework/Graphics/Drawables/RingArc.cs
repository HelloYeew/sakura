// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Vertex;
using Sakura.Framework.Maths;
using Sakura.Framework.Utilities;

namespace Sakura.Framework.Graphics.Drawables;

/// <summary>
/// A ring filled clockwise from 12 o'clock up to <see cref="Progress"/>: a circular progress bar. With a
/// <see cref="Start"/> it's an arc from there instead, as a segment of a ring split into bands.
/// The ring fills this drawable's bounds; <see cref="InnerRadius"/> sets its thickness.
/// </summary>
public partial class RingArc : Drawable
{
    /// <summary>
    /// Segments in a full turn; a partial ring uses its share.
    /// </summary>
    private const int full_segments = 160;

    private float start;
    private float progress = 1;
    private float innerRadius = 0.85f;
    private bool smoothEdges;

    /// <summary>
    /// Where the ring starts, 0–1 of a turn clockwise from 12 o'clock; it's drawn from here to <see cref="Progress"/>.
    /// </summary>
    public float Start
    {
        get => start;
        set
        {
            value = Math.Clamp(value, 0, 1);
            if (Precision.AlmostEquals(value, start))
                return;

            start = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    /// <summary>
    /// Where the ring ends, 0–1 of a turn: how much of it is drawn when it starts at 12 o'clock.
    /// </summary>
    public float Progress
    {
        get => progress;
        set
        {
            value = Math.Clamp(value, 0, 1);
            if (Precision.AlmostEquals(value, progress))
                return;

            progress = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    /// <summary>
    /// The inner edge as a fraction of the outer radius (0 is a pie, near 1 a hairline).
    /// </summary>
    public float InnerRadius
    {
        get => innerRadius;
        set
        {
            innerRadius = Math.Clamp(value, 0, 1);
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    /// <summary>
    /// Whether the edges fade over a screen pixel, as masks draw theirs, instead of stepping. It takes three times the
    /// vertices, so it's for the rings looked at closely, not for many on screen at once.
    /// </summary>
    public bool SmoothEdges
    {
        get => smoothEdges;
        set
        {
            if (value == smoothEdges)
                return;

            smoothEdges = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    protected internal override VertexTopology Topology => VertexTopology.Triangles;

    protected override void UpdateDrawColor() => UpdateVertexAlpha();

    protected override void GenerateVertices()
    {
        float sweepFraction = progress - start;
        int segments = (int)MathF.Ceiling(full_segments * sweepFraction);
        if (segments <= 0)
        {
            SetVertexCount(0);
            DrawRectangle = new RectangleF(0, 0, 0, 0);
            return;
        }

        int perSegment = FeatheredStrip.VerticesPerPiece(smoothEdges);
        SetVertexCount(segments * perSegment);

        var color = new Vector4(
            ColorExtensions.SrgbToLinear(Color.R),
            ColorExtensions.SrgbToLinear(Color.G),
            ColorExtensions.SrgbToLinear(Color.B),
            DrawAlpha * (Color.A / 255f));

        float startAngle = -MathF.PI / 2 + MathF.Tau * start;
        float sweep = MathF.Tau * sweepFraction;
        float feather = smoothEdges ? FeatheredStrip.EDGE_FEATHER : 0;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;

        StripSection previous = default;
        int offset = 0;

        for (int i = 0; i <= segments; i++)
        {
            // Clockwise from the start (screen y points down).
            float angle = startAngle + sweep * i / segments;
            float cos = MathF.Cos(angle);
            float sin = MathF.Sin(angle);

            // Unit-square local space, mapped to screen by ModelMatrix.
            Vector2 outer = Vector2.Transform(new Vector2(0.5f + 0.5f * cos, 0.5f + 0.5f * sin), ModelMatrix);
            Vector2 inner = Vector2.Transform(new Vector2(0.5f + 0.5f * innerRadius * cos, 0.5f + 0.5f * innerRadius * sin), ModelMatrix);
            var section = new StripSection(inner, outer, feather);

            minX = Math.Min(minX, Math.Min(section.AOuter.X, section.BOuter.X));
            minY = Math.Min(minY, Math.Min(section.AOuter.Y, section.BOuter.Y));
            maxX = Math.Max(maxX, Math.Max(section.AOuter.X, section.BOuter.X));
            maxY = Math.Max(maxY, Math.Max(section.AOuter.Y, section.BOuter.Y));

            if (i > 0)
                offset = FeatheredStrip.Write(Vertices, offset, previous, section, color, smoothEdges);

            previous = section;
        }

        DrawRectangle = new RectangleF(minX, minY, maxX - minX, maxY - minY);
    }
}
