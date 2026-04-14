// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Rendering.Vertex;
using Sakura.Framework.Maths;
using System;
using System.Collections.Generic;
using Sakura.Framework.Utilities;

namespace Sakura.Framework.Graphics.Drawables;

public class Path : Drawable
{
    private readonly List<Vector2> vertices = new();
    public IReadOnlyList<Vector2> PathVertices => vertices;

    private float thickness = 3f;
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

    private PathJointStyle jointStyle = PathJointStyle.Miter;
    public PathJointStyle JointStyle
    {
        get => jointStyle;
        set
        {
            if (jointStyle == value)
                return;
            jointStyle = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    /// <summary>
    /// The maximum multiplier for the miter length. If a corner is too sharp and the miter exceeds this limit,
    /// it will fallback to a flat cutoff (Bevel-like) to prevent massive spikes.
    /// </summary>
    public float MiterLimit { get; set; } = 3f;

    public void AddVertex(Vector2 position)
    {
        vertices.Add(position);
        updateBounds();
    }

    public void ClearVertices()
    {
        vertices.Clear();
        updateBounds();
    }

    private void updateBounds()
    {
        if (vertices.Count == 0) return;

        float maxX = 0;
        float maxY = 0;
        foreach (var v in vertices)
        {
            maxX = Math.Max(maxX, v.X);
            maxY = Math.Max(maxY, v.Y);
        }

        Size = new Vector2(maxX, maxY);
        Invalidate(InvalidationFlags.DrawInfo);
    }

    public void UpdateLastVertex(Vector2 position)
    {
        if (vertices.Count == 0) return;

        vertices[^1] = position;
        updateBounds();
    }

    protected override void GenerateVertices()
    {
        if (vertices.Count < 2)
        {
            Vertices = Array.Empty<Vertex>();
            return;
        }

        int requiredVertices = (vertices.Count - 1) * 6;
        if (Vertices.Length != requiredVertices)
        {
            Vertices = new Vertex[requiredVertices];
        }

        float rLinear = ColorExtensions.SrgbToLinear(Color.R);
        float gLinear = ColorExtensions.SrgbToLinear(Color.G);
        float bLinear = ColorExtensions.SrgbToLinear(Color.B);
        var calculatedColor = new Vector4(rLinear, gLinear, bLinear, DrawAlpha * (Color.A / 255f));

        var finalMatrix = ModelMatrix;
        float w = DrawSize.X > 0 ? DrawSize.X : 1;
        float h = DrawSize.Y > 0 ? DrawSize.Y : 1;

        Vector2[] p = new Vector2[vertices.Count];
        for (int i = 0; i < vertices.Count; i++)
        {
            p[i] = Vector2.Transform(new Vector2(vertices[i].X / w, vertices[i].Y / h), finalMatrix);
        }

        Vector2[] leftOut = new Vector2[vertices.Count];
        Vector2[] rightOut = new Vector2[vertices.Count];
        Vector2[] leftIn = new Vector2[vertices.Count];
        Vector2[] rightIn = new Vector2[vertices.Count];

        for (int i = 0; i < vertices.Count; i++)
        {
            Vector2 dIn = i > 0 ? Vector2.Normalize(p[i] - p[i - 1]) : Vector2.Zero;
            Vector2 dOut = i < vertices.Count - 1 ? Vector2.Normalize(p[i + 1] - p[i]) : Vector2.Zero;

            if (i == 0)
                dIn = dOut;
            if (i == vertices.Count - 1)
                dOut = dIn;

            Vector2 nIn = new Vector2(-dIn.Y, dIn.X);
            Vector2 nOut = new Vector2(-dOut.Y, dOut.X);

            if (JointStyle == PathJointStyle.Simple || i == 0 || i == vertices.Count - 1)
            {
                leftIn[i] = p[i] + nIn * (Thickness / 2f);
                rightIn[i] = p[i] - nIn * (Thickness / 2f);
                leftOut[i] = p[i] + nOut * (Thickness / 2f);
                rightOut[i] = p[i] - nOut * (Thickness / 2f);
            }
            else // Miter Joint
            {
                Vector2 tangent = Vector2.Normalize(dIn + dOut);
                if (tangent.X == 0 && tangent.Y == 0)
                    tangent = nIn;
                Vector2 miter = new Vector2(-tangent.Y, tangent.X);

                float dot = miter.X * nIn.X + miter.Y * nIn.Y;
                float length = Thickness / 2f;

                if (Math.Abs(dot) > 0.01f)
                    length = Thickness / 2f / dot;

                // Apply Miter Limit (Fallback to simple cutoff)
                if (Math.Abs(length) > MiterLimit * (Thickness / 2f))
                {
                    leftIn[i] = p[i] + nIn * (Thickness / 2f);
                    rightIn[i] = p[i] - nIn * (Thickness / 2f);
                    leftOut[i] = p[i] + nOut * (Thickness / 2f);
                    rightOut[i] = p[i] - nOut * (Thickness / 2f);
                }
                else
                {
                    Vector2 offset = miter * length;
                    leftIn[i] = leftOut[i] = p[i] + offset;
                    rightIn[i] = rightOut[i] = p[i] - offset;
                }
            }
        }

        // Generate Quads
        for (int i = 0; i < vertices.Count - 1; i++)
        {
            var v1 = leftOut[i];
            var v2 = rightOut[i];
            var v3 = rightIn[i + 1];
            var v4 = leftIn[i + 1];

            var topLeft = new Vertex
            {
                Position = v1,
                TexCoords = new Vector2(0, 0),
                Color = calculatedColor
            };
            var topRight = new Vertex
            {
                Position = v4,
                TexCoords = new Vector2(1, 0),
                Color = calculatedColor
            };
            var bottomLeft = new Vertex
            {
                Position = v2,
                TexCoords = new Vector2(0, 1),
                Color = calculatedColor
            };
            var bottomRight = new Vertex
            {
                Position = v3,
                TexCoords = new Vector2(1, 1),
                Color = calculatedColor
            };

            int offset = i * 6;
            Vertices[offset + 0] = topLeft;
            Vertices[offset + 1] = topRight;
            Vertices[offset + 2] = bottomRight;
            Vertices[offset + 3] = bottomRight;
            Vertices[offset + 4] = bottomLeft;
            Vertices[offset + 5] = topLeft;
        }
    }
}

/// <summary>
/// Defines how path segments are joined together at vertices. This affects the appearance of corners in the path.
/// </summary>
public enum PathJointStyle
{
    /// <summary>
    /// Segments are drawn independently without corner connections.
    /// </summary>
    Simple,

    /// <summary>
    /// Corners are extended to a sharp point.
    /// </summary>
    Miter
}
