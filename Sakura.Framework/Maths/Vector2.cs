// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Runtime.CompilerServices;
using SystemVector2 = System.Numerics.Vector2;

namespace Sakura.Framework.Maths;

[MathStruct]
public readonly partial struct Vector2
{
    public readonly SystemVector2 Value;

    public float X => Value.X;
    public float Y => Value.Y;

    public Vector2(float x, float y) => Value = new SystemVector2(x, y);

    public Vector2(float value) => Value = new SystemVector2(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Vector2(SystemVector2 v) => new Vector2(v.X, v.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator SystemVector2(Vector2 v) => v.Value;
}
