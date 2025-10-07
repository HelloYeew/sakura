// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.InteropServices;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Primitives;

/// <summary>
/// A 2D quadrilateral defined by four vertices.
/// <remarks>
/// The layout of vertices is essential to ensure correct rendering.
/// The vertices should be specified in clockwise order starting from the top-left vertex.
/// </remarks>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Quad : IEquatable<Quad>
{
    /// <summary>
    /// The top-left vertex of the quad.
    /// </summary>
    public Vector2 TopLeft;

    /// <summary>
    /// The top-right vertex of the quad.
    /// </summary>
    public Vector2 TopRight;

    /// <summary>
    /// The bottom-right vertex of the quad.
    /// </summary>
    public Vector2 BottomRight;

    /// <summary>
    /// The bottom-left vertex of the quad.
    /// </summary>
    public Vector2 BottomLeft;

    /// <summary>
    /// Creates a new quad with the specified vertices.
    /// The vertices should be specified in clockwise order.
    /// </summary>
    /// <param name="topLeft">The top-left vertex.</param>
    /// <param name="topRight">The top-right vertex.</param>
    /// <param name="bottomRight">The bottom-right vertex.</param>
    /// <param name="bottomLeft">The bottom-left vertex.</param>
    public Quad(Vector2 topLeft, Vector2 topRight, Vector2 bottomRight, Vector2 bottomLeft)
    {
        TopLeft = topLeft;
        TopRight = topRight;
        BottomRight = bottomRight;
        BottomLeft = bottomLeft;
    }

    /// <summary>
    /// Create a new quad from an axis-aligned rectangle (<see cref="RectangleF"/>)
    /// </summary>
    /// <param name="rectangle">The rectangle to create the quad from.</param>
    /// <returns>A new quad with vertices matching the rectangle's corners.</returns>
    public static Quad FromRectangle(RectangleF rectangle) =>
        new Quad(
            new Vector2(rectangle.X, rectangle.Y),
            new Vector2(rectangle.X + rectangle.Width, rectangle.Y),
            new Vector2(rectangle.X + rectangle.Width, rectangle.Y + rectangle.Height),
            new Vector2(rectangle.X, rectangle.Y + rectangle.Height)
        );

    /// <summary>
    /// Computes the axis-aligned bounding box that contains this quad.
    /// </summary>
    public RectangleF BoundingRectangle
    {
        get
        {
            float minX = Math.Min(TopLeft.X, Math.Min(TopRight.X, Math.Min(BottomLeft.X, BottomRight.X)));
            float minY = Math.Min(TopLeft.Y, Math.Min(TopRight.Y, Math.Min(BottomLeft.Y, BottomRight.Y)));
            float maxX = Math.Max(TopLeft.X, Math.Max(TopRight.X, Math.Max(BottomLeft.X, BottomRight.X)));
            float maxY = Math.Max(TopLeft.Y, Math.Max(TopRight.Y, Math.Max(BottomLeft.Y, BottomRight.Y)));

            return new RectangleF(minX, minY, maxX - minX, maxY - minY);
        }
    }

    public bool Equals(Quad other) => TopLeft.Equals(other.TopLeft) && TopRight.Equals(other.TopRight) && BottomRight.Equals(other.BottomRight) && BottomLeft.Equals(other.BottomLeft);
    public override bool Equals(object? obj) => obj is Quad other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(TopLeft, TopRight, BottomRight, BottomLeft);

    public static bool operator ==(Quad left, Quad right) => left.Equals(right);
    public static bool operator !=(Quad left, Quad right) => !left.Equals(right);

    public override string ToString() => $"Quad({TopLeft}, {TopRight}, {BottomRight}, {BottomLeft})";
}
