// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Primitives;

/// <summary>
/// Define padding or margin for the four sides of a rectangle.
/// </summary>
public struct MarginPadding
{
    public float Top;
    public float Right;
    public float Bottom;
    public float Left;

    public Vector2 TopLeft => new Vector2(Left, Top);
    public Vector2 BottomRight => new Vector2(Right, Bottom);

    public Vector2 Total => TopLeft + BottomRight;

    public MarginPadding(float all)
    {
        Top = Right = Bottom = Left = all;
    }

    public MarginPadding(float vertical, float horizontal)
    {
        Top = Bottom = vertical;
        Right = Left = horizontal;
    }

    public MarginPadding(float top, float right, float bottom, float left)
    {
        Top = top;
        Right = right;
        Bottom = bottom;
        Left = left;
    }

    public override string ToString() => $"(T:{Top}, R:{Right}, B:{Bottom}, L:{Left})";

    public override bool Equals(object? obj)
    {
        if (obj is not MarginPadding other) return false;
        return Math.Abs(Top - other.Top) < float.Epsilon &&
               Math.Abs(Right - other.Right) < float.Epsilon &&
               Math.Abs(Bottom - other.Bottom) < float.Epsilon &&
               Math.Abs(Left - other.Left) < float.Epsilon;
    }

    public override int GetHashCode() => HashCode.Combine(Top, Right, Bottom, Left);
}
