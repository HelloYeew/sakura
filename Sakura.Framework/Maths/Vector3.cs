// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Runtime.CompilerServices;
using SystemVector3 = System.Numerics.Vector3;
using SilkVector3 = Silk.NET.Maths.Vector3D<float>;

namespace Sakura.Framework.Maths;

/// <summary>
/// A 3D vector struct with implicit conversions to and from <see cref="System.Numerics.Vector3"/>.
/// </summary>
[MathStruct]
public partial struct Vector3
{
    public SystemVector3 Value;

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Vector3(SystemVector3 v) => new Vector3(v.X, v.Y, v.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator SystemVector3(Vector3 v) => v.Value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator SilkVector3(Vector3 v) => new SilkVector3(v.X, v.Y, v.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Vector3(SilkVector3 v) => new Vector3(v.X, v.Y, v.Z);
}
