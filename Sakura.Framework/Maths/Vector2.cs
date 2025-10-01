// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Runtime.CompilerServices;
using SystemVector2 = System.Numerics.Vector2;
using SilkVector2 = Silk.NET.Maths.Vector2D<float>;

namespace Sakura.Framework.Maths;

/// <summary>
/// A 2D vector struct with implicit conversions to and from <see cref="System.Numerics.Vector2"/>.
/// </summary>
[MathStruct]
public partial struct Vector2
{
    public SystemVector2 Value;

    public float X
    {
        get => Value.X;
        set => Value.X = value;
    }

    public float Y
    {
        get => Value.Y;
        set => Value.Y = value;
    }

    public static Vector2 Zero => new Vector2(0, 0);
    public static Vector2 One => new Vector2(1, 1);
    public static Vector2 UnitX => new Vector2(1, 0);
    public static Vector2 UnitY => new Vector2(0, 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Vector2(SystemVector2 v) => new Vector2(v.X, v.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator SystemVector2(Vector2 v) => v.Value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator SilkVector2(Vector2 v) => new SilkVector2(v.X, v.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Vector2(SilkVector2 v) => new Vector2(v.X, v.Y);
}
