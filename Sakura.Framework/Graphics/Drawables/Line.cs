// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Vertex;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Drawables;

/// <summary>
/// A drawable that render a line between two points with a specified thickness.
/// </summary>
public class Line : Drawable
{
    protected new readonly Vertex[] Vertices = new Vertex[6];

    private Vector2 startPoint = Vector2.Zero;
    public Vector2 StartPoint
    {
        get => startPoint;
        set
        {
            if (startPoint == value) return;
            startPoint = value;
            updateBounds();
        }
    }

    private Vector2 endPoint = Vector2.One;
    public Vector2 EndPoint
    {
        get => endPoint;
        set
        {
            if (endPoint == value) return;
            endPoint = value;
            updateBounds();
        }
    }

    private float thickness = 1f;
    public float Thickness
    {
        get => thickness;
        set
        {
            if (thickness == value) return;
            thickness = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    public Line()
    {
        updateBounds();
    }

    private void updateBounds()
    {
        Size = new Vector2(Math.Max(startPoint.X, endPoint.X), Math.Max(startPoint.Y, endPoint.Y));
        Invalidate(InvalidationFlags.DrawInfo);
    }

    protected override void GenerateVertices()
    {
        float rLinear = ColorExtensions.SrgbToLinear(Color.R);
        float gLinear = ColorExtensions.SrgbToLinear(Color.G);
        float bLinear = ColorExtensions.SrgbToLinear(Color.B);

        var calculatedColor = new System.Numerics.Vector4(rLinear, gLinear, bLinear, DrawAlpha * (Color.A / 255f));

        var finalMatrix = ModelMatrix;

        float w = DrawSize.X > 0 ? DrawSize.X : 1;
        float h = DrawSize.Y > 0 ? DrawSize.Y : 1;

        // Map pixel coordinates to the 0.0 -> 1.0 space, then transform
        var p1 = Vector2.Transform(new Vector2(startPoint.X / w, startPoint.Y / h), finalMatrix);
        var p2 = Vector2.Transform(new Vector2(endPoint.X / w, endPoint.Y / h), finalMatrix);

        Vector2 dir = p2 - p1;
        if (dir == Vector2.Zero) return;

        Vector2 normal = Vector2.Normalize(new Vector2(-dir.Y, dir.X));
        Vector2 offset = normal * (Thickness / 2f);

        var v1 = p1 - offset;
        var v2 = p2 - offset;
        var v3 = p2 + offset;
        var v4 = p1 + offset;

        var topLeft = new Vertex { Position = v4, TexCoords = new Vector2(0, 0), Color = calculatedColor };
        var topRight = new Vertex { Position = v3, TexCoords = new Vector2(1, 0), Color = calculatedColor };
        var bottomLeft = new Vertex { Position = v1, TexCoords = new Vector2(0, 1), Color = calculatedColor };
        var bottomRight = new Vertex { Position = v2, TexCoords = new Vector2(1, 1), Color = calculatedColor };

        Vertices[0] = topLeft;
        Vertices[1] = topRight;
        Vertices[2] = bottomRight;

        Vertices[3] = bottomRight;
        Vertices[4] = bottomLeft;
        Vertices[5] = topLeft;
    }

    public override void Draw(IRenderer renderer)
    {
        renderer.DrawVertices(Vertices, Texture ?? renderer.WhitePixel);
    }
}
