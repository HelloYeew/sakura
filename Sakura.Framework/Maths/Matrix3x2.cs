// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Runtime.CompilerServices;
using SystemMatrix3x2 = System.Numerics.Matrix3x2;
using SilkMatrix3x2 = Silk.NET.Maths.Matrix3X2<float>;

namespace Sakura.Framework.Maths;

/// <summary>
/// Represents a 3x2 matrix with implicit conversions to and from <see cref="System.Numerics.Matrix3x2"/>.
/// </summary>
[MathStruct]
public readonly partial struct Matrix3x2
{
    public readonly SystemMatrix3x2 Value;

    public float M11 => Value.M11;
    public float M12 => Value.M12;
    public float M21 => Value.M21;
    public float M22 => Value.M22;
    public float M31 => Value.M31;
    public float M32 => Value.M32;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Matrix3x2(SystemMatrix3x2 v) => new Matrix3x2(v.M11, v.M12, v.M21, v.M22, v.M31, v.M32);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator SystemMatrix3x2(Matrix3x2 v) => v.Value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator SilkMatrix3x2(Matrix3x2 v) => new SilkMatrix3x2(v.M11, v.M12, v.M21, v.M22, v.M31, v.M32);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Matrix3x2(SilkMatrix3x2 v) => new Matrix3x2(v.M11, v.M12, v.M21, v.M22, v.M31, v.M32);
}
