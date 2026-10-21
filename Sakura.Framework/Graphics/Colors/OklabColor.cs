// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Graphics.Colors;

/// <summary>
/// A color in Oklab, a space laid out by how different colors look: equal steps in it look like equal
/// steps, where equal steps in RGB numbers don't. Work in it to blend, lighten or compare colors by eye;
/// see <see cref="Extensions.ColorExtensions.ColorExtensions.BlendPerceptually"/> for the usual case.
/// </summary>
/// <param name="L">Perceived lightness, 0 (black) to 1 (white).</param>
/// <param name="A">Green (negative) to red (positive).</param>
/// <param name="B">Blue (negative) to yellow (positive).</param>
public readonly record struct OklabColor(double L, double A, double B)
{
    /// <summary>
    /// How colorful it is: its distance from the grey of the same lightness.
    /// </summary>
    public double Chroma => Math.Sqrt(A * A + B * B);

    /// <summary>
    /// <paramref name="color"/> in Oklab. Alpha isn't part of it; see <see cref="ToColor"/>.
    /// </summary>
    public static OklabColor FromColor(Color color)
    {
        double r = toLinear(color.R), g = toLinear(color.G), b = toLinear(color.B);

        double l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        double m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        double s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);

        return new OklabColor(
            0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
            1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
            0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
    }

    /// <summary>
    /// Back to an sRGB <see cref="Color"/> with <paramref name="alpha"/>. A color outside what sRGB can show
    /// is clamped into it, channel by channel.
    /// </summary>
    public Color ToColor(byte alpha = byte.MaxValue)
    {
        double l = Math.Pow(L + 0.3963377774 * A + 0.2158037573 * B, 3);
        double m = Math.Pow(L - 0.1055613458 * A - 0.0638541728 * B, 3);
        double s = Math.Pow(L - 0.0894841775 * A - 1.2914855480 * B, 3);

        return Color.FromArgb(
            alpha,
            toByte(4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s),
            toByte(-1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s),
            toByte(-0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s));
    }

    /// <summary>
    /// The color <paramref name="amount"/> (0 to 1, not clamped) of the way from <paramref name="from"/>
    /// to <paramref name="to"/>.
    /// </summary>
    public static OklabColor Lerp(OklabColor from, OklabColor to, double amount) => new OklabColor(
        from.L + (to.L - from.L) * amount,
        from.A + (to.A - from.A) * amount,
        from.B + (to.B - from.B) * amount);

    private static double toLinear(byte channel)
    {
        double c = channel / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static byte toByte(double linear)
    {
        double c = Math.Clamp(linear, 0, 1);
        double encoded = c <= 0.0031308 ? 12.92 * c : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055;
        return (byte)Math.Round(encoded * 255);
    }
}
