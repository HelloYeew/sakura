// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics.CodeAnalysis;
using Sakura.Framework.Extensions.ColorExtensions;

namespace Sakura.Framework.Graphics.Colors;

/// <summary>
/// A per-corner color for a quad. A solid color sets all four corners equal; gradients set them
/// differently and let the renderer interpolate across the quad (blending happens in linear space
/// on the GPU, since vertex colors are uploaded linear).
/// </summary>
/// <remarks>
/// The corner names match the quad vertex order used by
/// <see cref="Drawables.Drawable.GenerateVertices"/>
/// (0 = TopLeft, 1 = TopRight, 2 = BottomRight, 3 = BottomLeft).
/// </remarks>
public readonly struct ColorInfo : IEquatable<ColorInfo>
{
    public readonly Color TopLeft;
    public readonly Color TopRight;
    public readonly Color BottomLeft;
    public readonly Color BottomRight;

    public ColorInfo(Color topLeft, Color topRight, Color bottomLeft, Color bottomRight)
    {
        TopLeft = topLeft;
        TopRight = topRight;
        BottomLeft = bottomLeft;
        BottomRight = bottomRight;
    }

    /// <summary>
    /// Implicitly treats a single <see cref="Color"/> as a solid <see cref="ColorInfo"/> so existing
    /// <c>drawable.Color = someColour</c> call sites keep working.
    /// </summary>
    public static implicit operator ColorInfo(Color color) => Solid(color);

    /// <summary>
    /// A uniform color applied to all four corners.
    /// </summary>
    public static ColorInfo Solid(Color c) => new ColorInfo(c, c, c, c);

    /// <summary>
    /// A gradient interpolating from <paramref name="left"/> to <paramref name="right"/> along the X axis.
    /// </summary>
    public static ColorInfo GradientHorizontal(Color left, Color right) => new ColorInfo(left, right, left, right);

    /// <summary>
    /// A gradient interpolating from <paramref name="top"/> to <paramref name="bottom"/> along the Y axis.
    /// </summary>
    public static ColorInfo GradientVertical(Color top, Color bottom) => new ColorInfo(top, top, bottom, bottom);

    /// <summary>
    /// Whether all four corners are the same color (i.e. this is effectively a solid color).
    /// </summary>
    public bool HasSingleColour => TopLeft == TopRight && TopLeft == BottomLeft && TopLeft == BottomRight;

    /// <summary>
    /// Scales every corner's alpha by <paramref name="factor"/>, returning a new <see cref="ColorInfo"/>.
    /// </summary>
    public ColorInfo MultiplyAlpha(float factor)
    {
        if (factor < 0)
            throw new ArgumentOutOfRangeException(nameof(factor), factor, "Cannot multiply alpha by a negative value.");

        return new ColorInfo(
            multiplyAlpha(TopLeft, factor),
            multiplyAlpha(TopRight, factor),
            multiplyAlpha(BottomLeft, factor),
            multiplyAlpha(BottomRight, factor));
    }

    private static Color multiplyAlpha(Color c, float factor) => c.WithAlpha((byte)Math.Clamp(c.A * factor, 0, 255));

    /// <summary>
    /// Corner-wise linear interpolation between <paramref name="a"/> and <paramref name="b"/>, for
    /// animating between two <see cref="ColorInfo"/>s.
    /// </summary>
    public static ColorInfo Lerp(ColorInfo a, ColorInfo b, float t) => new ColorInfo(
        ColorExtensions.Lerp(a.TopLeft, b.TopLeft, t),
        ColorExtensions.Lerp(a.TopRight, b.TopRight, t),
        ColorExtensions.Lerp(a.BottomLeft, b.BottomLeft, t),
        ColorExtensions.Lerp(a.BottomRight, b.BottomRight, t));

    public bool Equals(ColorInfo other) =>
        TopLeft == other.TopLeft
        && TopRight == other.TopRight
        && BottomLeft == other.BottomLeft
        && BottomRight == other.BottomRight;

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is ColorInfo other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(TopLeft, TopRight, BottomLeft, BottomRight);

    public static bool operator ==(ColorInfo left, ColorInfo right) => left.Equals(right);

    public static bool operator !=(ColorInfo left, ColorInfo right) => !left.Equals(right);

    public override string ToString() =>
        HasSingleColour ? $"Solid({TopLeft})" : $"Gradient(TL={TopLeft}, TR={TopRight}, BL={BottomLeft}, BR={BottomRight})";
}
