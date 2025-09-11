// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Runtime.CompilerServices;
using SystemMatrix4x4 = System.Numerics.Matrix4x4;
using SilkMatrix4x4 = Silk.NET.Maths.Matrix4X4<float>;

namespace Sakura.Framework.Maths;

/// <summary>
/// A 4x4 matrix struct with implicit conversions to and from <see cref="System.Numerics.Matrix4x4"/>.
/// </summary>
[MathStruct]
public readonly partial struct Matrix4x4
{
    public readonly SystemMatrix4x4 Value;

    public float M11 => Value.M11;
    public float M12 => Value.M12;
    public float M13 => Value.M13;
    public float M14 => Value.M14;
    public float M21 => Value.M21;
    public float M22 => Value.M22;
    public float M23 => Value.M23;
    public float M24 => Value.M24;
    public float M31 => Value.M31;
    public float M32 => Value.M32;
    public float M33 => Value.M33;
    public float M34 => Value.M34;
    public float M41 => Value.M41;
    public float M42 => Value.M42;
    public float M43 => Value.M43;
    public float M44 => Value.M44;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Matrix4x4(SystemMatrix4x4 v) => new Matrix4x4(v.M11, v.M12, v.M13, v.M14, v.M21, v.M22, v.M23, v.M24, v.M31, v.M32, v.M33, v.M34, v.M41, v.M42, v.M43, v.M44);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator SystemMatrix4x4(Matrix4x4 v) => v.Value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator SilkMatrix4x4(Matrix4x4 v) => new SilkMatrix4x4(v.M11, v.M12, v.M13, v.M14, v.M21, v.M22, v.M23, v.M24, v.M31, v.M32, v.M33, v.M34, v.M41, v.M42, v.M43, v.M44);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Matrix4x4(SilkMatrix4x4 v) => new Matrix4x4(v.M11, v.M12, v.M13, v.M14, v.M21, v.M22, v.M23, v.M24, v.M31, v.M32, v.M33, v.M34, v.M41, v.M42, v.M43, v.M44);
}
