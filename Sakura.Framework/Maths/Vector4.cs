// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Runtime.CompilerServices;
using SystemVector4 = System.Numerics.Vector4;
using SilkVector4 = Silk.NET.Maths.Vector4D<float>;

namespace Sakura.Framework.Maths;

/// <summary>
/// A 4D vector struct with implicit conversions to and from <see cref="System.Numerics.Vector4"/>.
/// </summary>
[MathStruct]
public partial struct Vector4
{
    public SystemVector4 Value;

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

    public float Z
    {
        get => Value.Z;
        set => Value.Z = value;
    }

    public float W
    {
        get => Value.W;
        set => Value.W = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Vector4(SystemVector4 v) => new Vector4(v.X, v.Y, v.Z, v.W);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator SystemVector4(Vector4 v) => v.Value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator SilkVector4(Vector4 v) => new SilkVector4(v.X, v.Y, v.Z, v.W);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Vector4(SilkVector4 v) => new Vector4(v.X, v.Y, v.Z, v.W);
}
