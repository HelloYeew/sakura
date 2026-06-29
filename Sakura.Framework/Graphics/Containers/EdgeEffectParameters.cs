// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Containers;

/// <summary>
/// The type of edge effect to apply to a <see cref="Drawables.Container"/>.
/// </summary>
public enum EdgeEffectType
{
    /// <summary>
    /// No edge effect.
    /// </summary>
    None,

    /// <summary>
    /// A glow drawn on top of the container's contents (additive-style blending).
    /// </summary>
    Glow,

    /// <summary>
    /// A shadow drawn behind the container's contents (alpha blending).
    /// </summary>
    Shadow,
}

/// <summary>
/// Parameters describing an edge effect (a soft glow or drop shadow) rendered around the
/// rounded-rectangle shape of a <see cref="Drawables.Container"/>.
/// </summary>
public struct EdgeEffectParameters : IEquatable<EdgeEffectParameters>
{
    /// <summary>
    /// The colour of the edge effect (including its alpha). Premultiplied at draw time.
    /// </summary>
    public Color Colour;

    /// <summary>
    /// The offset of the edge effect from the container's shape, in local pixels.
    /// Typically used to push a <see cref="EdgeEffectType.Shadow"/> downwards.
    /// </summary>
    public Vector2 Offset;

    /// <summary>
    /// The type of the edge effect. <see cref="EdgeEffectType.None"/> disables rendering.
    /// </summary>
    public EdgeEffectType Type;

    /// <summary>
    /// The blur radius (soft falloff width) of the edge effect, in local pixels.
    /// </summary>
    public float Radius;

    /// <summary>
    /// Additional corner roundness added on top of the container's <see cref="Drawables.Container.CornerRadius"/>
    /// when shaping the effect.
    /// </summary>
    public float Roundness;

    /// <summary>
    /// Whether the inner area of the effect (the part covered by the container) should be cut out,
    /// leaving only the surrounding ring. Useful for outline-style glows.
    /// </summary>
    public bool Hollow;

    public readonly bool Equals(EdgeEffectParameters other) =>
        Colour.Equals(other.Colour)
        && Offset.Equals(other.Offset)
        && Type == other.Type
        && Radius == other.Radius
        && Roundness == other.Roundness
        && Hollow == other.Hollow;

    public override readonly bool Equals(object? obj) => obj is EdgeEffectParameters other && Equals(other);

    public override readonly int GetHashCode() => HashCode.Combine(Colour, Offset, (int)Type, Radius, Roundness, Hollow);

    public static bool operator ==(EdgeEffectParameters left, EdgeEffectParameters right) => left.Equals(right);

    public static bool operator !=(EdgeEffectParameters left, EdgeEffectParameters right) => !left.Equals(right);
}
