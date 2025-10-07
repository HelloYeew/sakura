// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Maths;

public struct RectangleF
{
    public float X;
    public float Y;
    public float Width;
    public float Height;

    public Vector2 Location
    {
        get => new Vector2(X, Y);
        set
        {
            X = value.X;
            Y = value.Y;
        }
    }

    public Vector2 Size
    {
        get => new Vector2(Width, Height);
        set
        {
            Width = value.X;
            Height = value.Y;
        }
    }

    public bool Contains(Vector2 point)
    {
        return point.X >= X && point.X <= X + Width &&
               point.Y >= Y && point.Y <= Y + Height;
    }

    public RectangleF(float x, float y, float width, float height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public RectangleF(Vector2 location, Vector2 size)
    {
        X = location.X;
        Y = location.Y;
        Width = size.X;
        Height = size.Y;
    }

    public override string ToString()
    {
        return $"[X={X}, Y={Y}, Width={Width}, Height={Height}]";
    }
}
