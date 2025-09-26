// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using SystemMatrix4x4 = System.Numerics.Matrix4x4;
using SilkMatrix4x4 = Silk.NET.Maths.Matrix4X4<float>;

namespace Sakura.Framework.Maths;

/// <summary>
/// A 4x4 matrix struct with implicit conversions to and from <see cref="System.Numerics.Matrix4x4"/>.
/// </summary>
[MathStruct]
[SuppressMessage("ReSharper", "InconsistentNaming")]
public partial struct Matrix4x4
{
    public SystemMatrix4x4 Value;

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

    public float M13
    {
        get => Value.M13;
        set => Value.M13 = value;
    }

    public float M14
    {
        get => Value.M14;
        set => Value.M14 = value;
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

    public float M23
    {
        get => Value.M23;
        set => Value.M23 = value;
    }

    public float M24
    {
        get => Value.M24;
        set => Value.M24 = value;
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

    public float M33
    {
        get => Value.M33;
        set => Value.M33 = value;
    }

    public float M34
    {
        get => Value.M34;
        set => Value.M34 = value;
    }

    public float M41
    {
        get => Value.M41;
        set => Value.M41 = value;
    }

    public float M42
    {
        get => Value.M42;
        set => Value.M42 = value;
    }

    public float M43
    {
        get => Value.M43;
        set => Value.M43 = value;
    }

    public float M44
    {
        get => Value.M44;
        set => Value.M44 = value;
    }

    public static Matrix4x4 Identity => new Matrix4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Matrix4x4(SystemMatrix4x4 v) => new Matrix4x4(v.M11, v.M12, v.M13, v.M14, v.M21, v.M22, v.M23, v.M24, v.M31, v.M32, v.M33, v.M34, v.M41, v.M42, v.M43, v.M44);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator SystemMatrix4x4(Matrix4x4 v) => v.Value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator SilkMatrix4x4(Matrix4x4 v) => new SilkMatrix4x4(v.M11, v.M12, v.M13, v.M14, v.M21, v.M22, v.M23, v.M24, v.M31, v.M32, v.M33, v.M34, v.M41, v.M42, v.M43, v.M44);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Matrix4x4(SilkMatrix4x4 v) => new Matrix4x4(v.M11, v.M12, v.M13, v.M14, v.M21, v.M22, v.M23, v.M24, v.M31, v.M32, v.M33, v.M34, v.M41, v.M42, v.M43, v.M44);
}
