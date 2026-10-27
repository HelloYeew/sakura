// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using SystemMatrix3x2 = System.Numerics.Matrix3x2;
using SilkMatrix3x2 = Silk.NET.Maths.Matrix3X2<float>;

namespace Sakura.Framework.Maths;

/// <summary>
/// Represents a 3x2 matrix with implicit conversions to and from <see cref="System.Numerics.Matrix3x2"/>.
/// </summary>
[MathStruct]
[SuppressMessage("ReSharper", "InconsistentNaming")]
public partial struct Matrix3x2
{
    public SystemMatrix3x2 Value;

    public float M11
    {
        get => Value.M11;
        set => Value.M11 = value;
    }

    public float M12
    {
        get => Value.M12;
        set => Value.M12 = value;
    }

    public float M21
    {
        get => Value.M21;
        set => Value.M21 = value;
    }

    public float M22
    {
        get => Value.M22;
        set => Value.M22 = value;
    }

    public float M31
    {
        get => Value.M31;
        set => Value.M31 = value;
    }

    public float M32
    {
        get => Value.M32;
        set => Value.M32 = value;
    }

    public static Matrix3x2 Identity => new Matrix3x2(1, 0, 0, 1, 0, 0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Matrix3x2(SystemMatrix3x2 v) => new Matrix3x2(v.M11, v.M12, v.M21, v.M22, v.M31, v.M32);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator SystemMatrix3x2(Matrix3x2 v) => v.Value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator SilkMatrix3x2(Matrix3x2 v) => new SilkMatrix3x2(v.M11, v.M12, v.M21, v.M22, v.M31, v.M32);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Matrix3x2(SilkMatrix3x2 v) => new Matrix3x2(v.M11, v.M12, v.M21, v.M22, v.M31, v.M32);

    // Bypass fix of https://github.com/dotnet/runtime/pull/134105 until it release on .NET 12
    // See https://github.com/HelloYeew/sakura/pull/206 for more details

    /// <summary>
    /// Multiplies two matrices together to compute the product.
    /// </summary>
    /// <param name="value1">The first matrix.</param>
    /// <param name="value2">The second matrix.</param>
    /// <returns>The product matrix: <paramref name="value1" /> applied first, then <paramref name="value2" />.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Matrix3x2 operator *(Matrix3x2 value1, Matrix3x2 value2)
    {
        ref readonly var left = ref value1.Value;
        ref readonly var right = ref value2.Value;

        // Each row of the left one, transformed by the right one.
        var product = new SystemMatrix3x2
        {
            X = right.X * left.M11 + right.Y * left.M12,
            Y = right.X * left.M21 + right.Y * left.M22,
            Z = right.X * left.M31 + right.Y * left.M32 + right.Z,
        };

        return new Matrix3x2 { Value = product };
    }

    /// <summary>Multiplies two matrices together to compute the product.</summary>
    /// <param name="value1">The first matrix.</param>
    /// <param name="value2">The second matrix.</param>
    /// <returns>The product matrix.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Matrix3x2 Multiply(Matrix3x2 value1, Matrix3x2 value2) => value1 * value2;
}
