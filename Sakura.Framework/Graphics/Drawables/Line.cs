// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Vertex;
using Sakura.Framework.Graphics.Textures;
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
            Invalidate(InvalidationFlags.DrawInfo);
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
            Invalidate(InvalidationFlags.DrawInfo);
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

    protected override void GenerateVertices()
    {
        var calculatedColor = new Vector4(Color.R / 255f, Color.G / 255f, Color.B / 255f, Alpha);

        // Transform start and end points into the parent's coordinate space.
        // Do this by creating a matrix that scales by our size, then multiplying by the main model matrix.
        var sizeMatrix = Matrix4x4.CreateScale(new Vector3(DrawSize.X, DrawSize.Y, 1));
        var finalMatrix = sizeMatrix * ModelMatrix;

        var p1 = Vector2.Transform(startPoint, finalMatrix);
        var p2 = Vector2.Transform(endPoint, finalMatrix);

        // Calculate the direction and a perpendicular normal for the line's thickness
        Vector2 dir = p2 - p1;
        if (dir == Vector2.Zero) return; // Cannot draw a zero-length line

        Vector2 normal = Vector2.Normalize(new Vector2(-dir.Y, dir.X));
        Vector2 offset = normal * (Thickness / 2f);

        // Calculate the four corners of the quad
        var v1 = p1 - offset;
        var v2 = p2 - offset;
        var v3 = p2 + offset;
        var v4 = p1 + offset;

        // Create the vertex objects
        var topLeft = new Vertex { Position = v4, TexCoords = new Vector2(0, 0), Color = calculatedColor };
        var topRight = new Vertex { Position = v3, TexCoords = new Vector2(1, 0), Color = calculatedColor };
        var bottomLeft = new Vertex { Position = v1, TexCoords = new Vector2(0, 1), Color = calculatedColor };
        var bottomRight = new Vertex { Position = v2, TexCoords = new Vector2(1, 1), Color = calculatedColor };

        // Triangle 1
        Vertices[0] = topLeft;
        Vertices[1] = topRight;
        Vertices[2] = bottomRight;

        // Triangle 2
        Vertices[3] = bottomRight;
        Vertices[4] = bottomLeft;
        Vertices[5] = topLeft;
    }

    public override void Draw(IRenderer renderer)
    {
        renderer.DrawVertices(Vertices, Texture ?? TextureGL.WhitePixel);
    }
}
