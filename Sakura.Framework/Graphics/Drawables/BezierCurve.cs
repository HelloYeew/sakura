// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Rendering.Vertex;
using Sakura.Framework.Maths;
using Sakura.Framework.Utilities;

namespace Sakura.Framework.Graphics.Drawables;

public class BezierCurve : Drawable
{
    private Vector2 p0 = Vector2.Zero;
    private Vector2 p1 = new Vector2(50, 0);
    private Vector2 p2 = new Vector2(50, 100);
    private Vector2 p3 = new Vector2(100, 100);

    private float thickness = 3f;
    private const int segments = 25;

    public Vector2 P0
    {
        get => p0;
        set {
            if (p0 == value)
                return;
            p0 = value;
            updateBounds();
        }
    }
    public Vector2 P1
    {
        get => p1;
        set
        {
            if (p1 == value)
                return;
            p1 = value;
            updateBounds();
        }
    }
    public Vector2 P2
    {
        get => p2;
        set
        {
            if (p2 == value)
                return;
            p2 = value;
            updateBounds();
        }
    }
    public Vector2 P3
    {
        get => p3;
        set
        {
            if (p3 == value)
                return;
            p3 = value;
            updateBounds();
        }
    }

    public float Thickness
    {
        get => thickness;
        set
        {
            if (Precision.AlmostEquals(thickness, value))
                return;
            thickness = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    public BezierCurve()
    {
        updateBounds();
    }

    private void updateBounds()
    {
        float maxX = Math.Max(Math.Max(P0.X, P1.X), Math.Max(P2.X, P3.X));
        float maxY = Math.Max(Math.Max(P0.Y, P1.Y), Math.Max(P2.Y, P3.Y));
        Size = new Vector2(maxX, maxY);

        Invalidate(InvalidationFlags.DrawInfo);
    }

    protected override void GenerateVertices()
    {
        int requiredVertices = segments * 6;
        if (Vertices.Length != requiredVertices)
        {
            Vertices = new Vertex[requiredVertices];
        }

        float rLinear = ColorExtensions.SrgbToLinear(Color.R);
        float gLinear = ColorExtensions.SrgbToLinear(Color.G);
        float bLinear = ColorExtensions.SrgbToLinear(Color.B);
        var calculatedColor = new Vector4(rLinear, gLinear, bLinear, DrawAlpha * (Color.A / 255f));

        var finalMatrix = ModelMatrix;

        // Prevent division by zero
        float w = DrawSize.X > 0 ? DrawSize.X : 1;
        float h = DrawSize.Y > 0 ? DrawSize.Y : 1;

        // Normalize the points into the 0.0 -> 1.0 space expected by ModelMatrix
        var t0 = Vector2.Transform(new Vector2(p0.X / w, p0.Y / h), finalMatrix);
        var t1 = Vector2.Transform(new Vector2(p1.X / w, p1.Y / h), finalMatrix);
        var t2 = Vector2.Transform(new Vector2(p2.X / w, p2.Y / h), finalMatrix);
        var t3 = Vector2.Transform(new Vector2(p3.X / w, p3.Y / h), finalMatrix);

        Vector2 prevPoint = t0;

        for (int i = 1; i <= segments; i++)
        {
            float t = i / (float)segments;
            Vector2 currentPoint = calculateCubicBezier(t0, t1, t2, t3, t);

            Vector2 dir = new Vector2(currentPoint.X - prevPoint.X, currentPoint.Y - prevPoint.Y);
            if (dir.X == 0 && dir.Y == 0)
                dir = new Vector2(1, 0);

            Vector2 normal = Vector2.Normalize(new Vector2(-dir.Y, dir.X));
            Vector2 offset = new Vector2(normal.X * (Thickness / 2f), normal.Y * (Thickness / 2f));

            var v1 = new Vector2(prevPoint.X - offset.X, prevPoint.Y - offset.Y);
            var v2 = new Vector2(currentPoint.X - offset.X, currentPoint.Y - offset.Y);
            var v3 = new Vector2(currentPoint.X + offset.X, currentPoint.Y + offset.Y);
            var v4 = new Vector2(prevPoint.X + offset.X, prevPoint.Y + offset.Y);

            var topLeft = new Vertex
            {
                Position = v4,
                TexCoords = new Vector2(0, 0),
                Color = calculatedColor
            };
            var topRight = new Vertex
            {
                Position = v3,
                TexCoords = new Vector2(1, 0),
                Color = calculatedColor
            };
            var bottomLeft = new Vertex
            {
                Position = v1,
                TexCoords = new Vector2(0, 1),
                Color = calculatedColor
            };
            var bottomRight = new Vertex
            {
                Position = v2,
                TexCoords = new Vector2(1, 1),
                Color = calculatedColor
            };

            int vertexOffset = (i - 1) * 6;

            Vertices[vertexOffset + 0] = topLeft;
            Vertices[vertexOffset + 1] = topRight;
            Vertices[vertexOffset + 2] = bottomRight;
            Vertices[vertexOffset + 3] = bottomRight;
            Vertices[vertexOffset + 4] = bottomLeft;
            Vertices[vertexOffset + 5] = topLeft;

            prevPoint = currentPoint;
        }
    }

    /// <summary>
    /// Calculates a point on a cubic bezier curve defined by four control points and a parameter t (0 <= t <= 1).
    /// </summary>
    /// <param name="point0">First control point (start of the curve)</param>
    /// <param name="point1">Second control point</param>
    /// <param name="point2">Third control point</param>
    /// <param name="point3">Fourth control point (end of the curve)</param>
    /// <param name="t">Parameter between 0 and 1 indicating the position along the curve</param>
    /// <returns>The point on the bezier curve corresponding to the parameter t</returns>
    private static Vector2 calculateCubicBezier(Vector2 point0, Vector2 point1, Vector2 point2, Vector2 point3, float t)
    {
        float u = 1 - t;
        float tt = t * t;
        float uu = u * u;
        float uuu = uu * u;
        float ttt = tt * t;

        float x = uuu * point0.X + 3 * uu * t * point1.X + 3 * u * tt * point2.X + ttt * point3.X;
        float y = uuu * point0.Y + 3 * uu * t * point1.Y + 3 * u * tt * point2.Y + ttt * point3.Y;

        return new Vector2(x, y);
    }
}
