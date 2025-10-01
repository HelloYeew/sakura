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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Matrix3x2(SystemMatrix3x2 v) => new Matrix3x2(v.M11, v.M12, v.M21, v.M22, v.M31, v.M32);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator SystemMatrix3x2(Matrix3x2 v) => v.Value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator SilkMatrix3x2(Matrix3x2 v) => new SilkMatrix3x2(v.M11, v.M12, v.M21, v.M22, v.M31, v.M32);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Matrix3x2(SilkMatrix3x2 v) => new Matrix3x2(v.M11, v.M12, v.M21, v.M22, v.M31, v.M32);
}
