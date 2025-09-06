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
public readonly partial struct Vector2
{
    public readonly SystemVector2 Value;

    public float X => Value.X;
    public float Y => Value.Y;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Vector2(SystemVector2 v) => new Vector2(v.X, v.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator SystemVector2(Vector2 v) => v.Value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator SilkVector2(Vector2 v) => new SilkVector2(v.X, v.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Vector2(SilkVector2 v) => new Vector2(v.X, v.Y);
}
