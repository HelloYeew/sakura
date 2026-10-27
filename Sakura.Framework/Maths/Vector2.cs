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

    // Bypass fix of https://github.com/dotnet/runtime/pull/134105 until it release on .NET 12
    // See https://github.com/HelloYeew/sakura/pull/206 for more details

    /// <summary>Transforms a vector by a specified 3x2 matrix.</summary>
    /// <param name="position">The vector to transform.</param>
    /// <param name="matrix">The transformation matrix.</param>
    /// <returns>The transformed vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Transform(Vector2 position, Matrix3x2 matrix)
        => new Vector2 { Value = matrix.Value.X * position.X + matrix.Value.Y * position.Y + matrix.Value.Z };

    /// <summary>Transforms a vector normal by the given 3x2 matrix, leaving out its translation.</summary>
    /// <param name="normal">The source vector.</param>
    /// <param name="matrix">The matrix.</param>
    /// <returns>The transformed vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 TransformNormal(Vector2 normal, Matrix3x2 matrix)
        => new Vector2 { Value = matrix.Value.X * normal.X + matrix.Value.Y * normal.Y };

    /// <summary>Performs a linear interpolation between two vectors based on the given weighting.</summary>
    /// <param name="value1">The first vector.</param>
    /// <param name="value2">The second vector.</param>
    /// <param name="amount">A value between 0 and 1 that indicates the weight of <paramref name="value2" />.</param>
    /// <returns>The interpolated vector, exactly <paramref name="value2" /> at an <paramref name="amount" /> of 1.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Lerp(Vector2 value1, Vector2 value2, float amount)
        => new Vector2 { Value = value1.Value * (1f - amount) + value2.Value * amount };
}
