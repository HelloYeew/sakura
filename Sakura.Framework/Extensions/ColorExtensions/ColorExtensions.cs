// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Colors;

namespace Sakura.Framework.Extensions.ColorExtensions;

/// <summary>
/// Provides extension methods for frequently used color operations.
/// </summary>
public static class ColorExtensions
{
    /// <summary>
    /// Creates a <see cref="Color"/> from a hex string. Supports formats: #RRGGBB and #AARRGGBB.
    /// </summary>
    /// <param name="hex">The hex string representing the color.</param>
    /// <returns>The corresponding <see cref="Color"/></returns>
    /// <exception cref="ArgumentException">Thrown if the hex string is not in a valid format.</exception>
    public static Color FromHex(string hex)
    {
        // Remove the '#' character if present
        if (hex.StartsWith("#"))
            hex = hex[1..];

        if (hex.Length != 6 && hex.Length != 8)
        {
            throw new ArgumentException("Hex string must be 6 or 8 characters long.");
        }

        byte a = 255; // Default alpha value
        int startIndex = 0;
        if (hex.Length == 8)
        {
            a = Convert.ToByte(hex.Substring(0, 2), 16);
            startIndex = 2;
        }
        byte r = Convert.ToByte(hex.Substring(startIndex, 2), 16);
        byte g = Convert.ToByte(hex.Substring(startIndex + 2, 2) , 16);
        byte b = Convert.ToByte(hex.Substring(startIndex + 4, 2), 16);
        return Color.FromArgb(a, r, g, b);
    }

    /// <summary>
    /// Converts a <see cref="Color"/> to its hex string representation. Optionally includes the alpha channel.
    /// </summary>
    /// <param name="color">The <see cref="Color"/> to convert.</param>
    /// <param name="includeAlpha">Whether to include the alpha channel in the hex string.</param>
    /// <returns></returns>
    public static string ToHex(this Color color, bool includeAlpha = false)
    {
        return includeAlpha
            ? $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}"
            : $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    /// <summary>
    /// Creates a <see cref="Color"/> from RGB or RGBA byte values.
    /// </summary>
    /// <param name="r">Red component (0-255)</param>
    /// <param name="g">Green component (0-255)</param>
    /// <param name="b">Blue component (0-255)</param>
    /// <param name="a">Alpha component (0-255), default is 255 (opaque)</param>
    /// <returns>The corresponding <see cref="Color"/></returns>
    public static Color FromRgb(byte r, byte g, byte b, byte a = 255)
    {
        return Color.FromArgb(a, r, g, b);
    }

    /// <summary>
    /// Creates a <see cref="Color"/> from RGBA byte values.
    /// </summary>
    /// <param name="r">Red component (0-255)</param>
    /// <param name="g">Green component (0-255)</param>
    /// <param name="b">Blue component (0-255)</param>
    /// <param name="a">Alpha component (0-255)</param>
    /// <returns>The corresponding <see cref="Color"/></returns>
    public static Color FromRgba(byte r, byte g, byte b, byte a)
    {
        return Color.FromArgb(a, r, g, b);
    }

    /// <summary>
    /// Returns a new <see cref="Color"/> with the specified alpha value.
    /// </summary>
    /// <param name="color">The original color.</param>
    /// <param name="alpha">The new alpha value (0-255).</param>
    /// <returns>The color with the updated alpha value.</returns>
    public static Color WithAlpha(this Color color, byte alpha)
    {
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    /// <summary>
    /// Darkens the color by the specified factor (0 to 1).
    /// A factor of 0 returns the original color, while a factor of 1 returns
    /// </summary>
    /// <param name="color">The original color.</param>
    /// <param name="factor">The darkening factor (0 to 1).</param>
    /// <returns>The darkened color.</returns>
    public static Color Darken(this Color color, float factor)
    {
        factor = Math.Clamp(factor, 0, 1);
        return Color.FromArgb(color.A,
            (byte)(color.R * (1 - factor)),
            (byte)(color.G * (1 - factor)),
            (byte)(color.B * (1 - factor)));
    }

    /// <summary>
    /// Lightens the color by the specified factor (0 to 1).
    /// </summary>
    /// <param name="color">The original color.</param>
    /// <param name="factor">The lightening factor (0 to 1).</param>
    /// <returns>The lightened color.</returns>
    public static Color Lighten(this Color color, float factor)
    {
        factor = Math.Clamp(factor, 0, 1);
        return Color.FromArgb(color.A,
            (byte)(color.R + (255 - color.R) * factor),
            (byte)(color.G + (255 - color.G) * factor),
            (byte)(color.B + (255 - color.B) * factor));
    }

    /// <summary>
    /// Blends the color with another color by the specified factor (0 to 1).
    /// </summary>
    /// <param name="color">The original color.</param>
    /// <param name="other">The color to blend with.</param>
    /// <param name="factor">The blending factor (0 to 1).</param>
    /// <returns>The blended color.</returns>
    public static Color Blend(this Color color, Color other, float factor)
    {
        factor = Math.Clamp(factor, 0, 1);
        return Color.FromArgb(
            (byte)(color.A + (other.A - color.A) * factor),
            (byte)(color.R + (other.R - color.R) * factor),
            (byte)(color.G + (other.G - color.G) * factor),
            (byte)(color.B + (other.B - color.B) * factor));
    }

    /// <summary>
    /// Calculates the brightness of the color (0 to 1).
    /// 0 is black, 1 is white.
    /// </summary>
    /// <param name="color">Color to evaluate.</param>
    /// <returns>Brightness value (0 to 1).</returns>
    public static float GetBrightness(this Color color)
    {
        return (0.299f * color.R + 0.587f * color.G + 0.114f * color.B) / 255f;
    }

    /// <summary>
    /// Determines if the color is considered "dark" based on its brightness.
    /// A color is considered dark if its brightness is less than 0.5.
    /// </summary>
    /// <param name="color">Color to evaluate.</param>
    /// <returns>True if the color is dark, false otherwise.</returns>
    public static bool IsDark(this Color color)
    {
        return color.GetBrightness() < 0.5f;
    }

    /// <summary>
    /// Determines if the color is considered "light" based on its brightness.
    /// A color is considered light if its brightness is 0.5 or greater.
    /// </summary>
    /// <param name="color">Color to evaluate.</param>
    /// <returns>True if the color is light, false otherwise.</returns>
    public static bool IsLight(this Color color)
    {
        return color.GetBrightness() >= 0.5f;
    }

    /// <summary>
    /// Inverts the color.
    /// </summary>
    /// <param name="color">The original color.</param>
    /// <returns>The inverted color.</returns>
    public static Color Invert(this Color color)
    {
        return Color.FromArgb(color.A, (byte)(255 - color.R), (byte)(255 - color.G), (byte)(255 - color.B));
    }

    /// <summary>
    /// Converts the color to grayscale using the luminosity method.
    /// </summary>
    /// <param name="color">The original color.</param>
    /// <returns>The grayscale color.</returns>
    public static Color ToGrayscale(this Color color)
    {
        byte gray = (byte)(0.299 * color.R + 0.587 * color.G + 0.114 * color.B);
        return Color.FromArgb(color.A, gray, gray, gray);
    }

    /// <summary>
    /// Returns a new <see cref="Color"/> with the specified red, green, blue, or alpha component replaced.
    /// </summary>
    /// <param name="color">The original color.</param>
    /// <param name="red">The new red component (0-255).</param>
    /// <returns>The color with the updated red component.</returns>
    public static Color WithRed(this Color color, byte red) => Color.FromArgb(color.A, red, color.G, color.B);

    /// <summary>
    /// Returns a new <see cref="Color"/> with the specified green component replaced.
    /// </summary>
    /// <param name="color">The original color.</param>
    /// <param name="green">The new green component (0-255).</param>
    /// <returns>The color with the updated green component.</returns>
    public static Color WithGreen(this Color color, byte green) => Color.FromArgb(color.A , color.R, green, color.B);

    /// <summary>
    /// Returns a new <see cref="Color"/> with the specified blue component replaced.
    /// </summary>
    /// <param name="color">The original color.</param>
    /// <param name="blue">The new blue component (0-255).</param>
    /// <returns>The color with the updated blue component.</returns>
    public static Color WithBlue(this Color color, byte blue) => Color.FromArgb(color.A, color.R, color.G, blue);

    /// <summary>
    /// Returns a new <see cref="Color"/> with the specified alpha value (0 to 1).
    /// </summary>
    /// <param name="color">The original color.</param>
    /// <param name="alpha">The new alpha value (0 to 1).</param>
    /// <returns>The color with the updated alpha value.</returns>
    public static Color WithAlpha(this Color color, float alpha)
    {
        alpha = Math.Clamp(alpha, 0, 1);
        return Color.FromArgb((byte)(alpha * 255), color.R, color.G, color.B);
    }

    /// <summary>
    /// Converts the color to HSL (Hue, Saturation, Lightness) representation.
    /// </summary>
    /// <param name="color">The color to convert.</param>
    /// <param name="h">Hue component (0 to 1).</param>
    /// <param name="s">Saturation component (0 to 1).</param>
    /// <param name="l">Lightness component (0 to 1).</param>
    public static void ToHSL(Color color, out float h, out float s, out float l)
    {
        float r = color.R / 255f;
        float g = color.G / 255f;
        float b = color.B / 255f;

        float max = Math.Max(r, Math.Max(g, b));
        float min = Math.Min(r, Math.Min(g, b));
        l = (max + min) / 2;

        if (max == min)
        {
            h = s = 0; // achromatic
        }
        else
        {
            float d = max - min;
            s = l > 0.5f ? d / (2 - max - min) : d / (max + min);

            if (max == r)
                h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g)
                h = (b - r) / d + 2;
            else
                h = (r - g) / d + 4;

            h /= 6;
        }
    }

    /// <summary>
    /// Creates a <see cref="Color"/> from HSL (Hue, Saturation, Lightness) values.
    /// Hue, Saturation, and Lightness should be in the range of 0 to 1.
    /// </summary>
    /// <param name="h">Hue component (0 to 1).</param>
    /// <param name="s">Saturation component (0 to 1).</param>
    /// <param name="l">Lightness component (0 to 1).</param>
    /// <param name="alpha">Alpha component (0 to 255), default is 255 (opaque).</param>
    /// <returns>The corresponding <see cref="Color"/></returns>
    public static Color FromHSL(float h, float s, float l, byte alpha = 255)
    {
        byte r, g, b;

        if (s == 0)
        {
            r = g = b = (byte)(l * 255); // achromatic
        }
        else
        {
            float q = l < 0.5f ? l * (1 + s) : l + s - l * s;
            float p = 2 * l - q;
            r = (byte)(HueToRGB(p, q, h + 1f / 3) * 255);
            g = (byte)(HueToRGB(p, q, h) * 255);
            b = (byte)(HueToRGB(p, q, h - 1f / 3) * 255);
        }

        return Color.FromArgb(alpha, r, g, b);
    }

    /// <summary>
    /// Helper method for HSL to RGB conversion.
    /// </summary>
    /// <param name="p"></param>
    /// <param name="q"></param>
    /// <param name="t"></param>
    /// <returns></returns>
    private static float HueToRGB(float p, float q, float t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1f / 6) return p + (q - p) * 6 * t;
        if (t < 1f / 2) return q;
        if (t < 2f / 3) return p + (q - p) * (2f / 3 - t) * 6;
        return p;
    }

    /// <summary>
    /// Returns a new <see cref="Color"/> with the specified hue (0 to 1) while preserving saturation and lightness.
    /// </summary>
    /// <param name="color">The original color.</param>
    /// <param name="hue">The new hue value (0 to 1).</param>
    /// <returns>The color with the updated hue value.</returns>
    public static Color WithHue(this Color color, float hue)
    {
        float s, l;
        ToHSL(color, out _, out s, out l);
        return FromHSL(hue, s, l, color.A);
    }

    /// <summary>
    /// Returns a new <see cref="Color"/> with the specified saturation (0 to 1) while preserving hue and lightness.
    /// </summary>
    /// <param name="color">The original color.</param>
    /// <param name="saturation">The new saturation value (0 to 1).</param>
    /// <returns>The color with the updated saturation value.</returns>
    public static Color WithSaturation(this Color color, float saturation)
    {
        float h, l;
        ToHSL(color, out h, out _, out l);
        return FromHSL(h, saturation, l, color.A);
    }

    /// <summary>
    /// Returns a new <see cref="Color"/> with the specified lightness (0 to 1) while preserving hue and saturation.
    /// </summary>
    /// <param name="color">The original color.</param>
    /// <param name="lightness">The new lightness value (0 to 1).</param>
    /// <returns>The color with the updated lightness value.</returns>
    public static Color WithLightness(this Color color, float lightness)
    {
        float h, s;
        ToHSL(color, out h, out s, out _);
        return FromHSL(h, s, lightness, color.A);
    }

    /// <summary>
    /// Linearly interpolates between two colors by the specified factor t (0 to 1).
    /// </summary>
    /// <param name="a">Color a.</param>
    /// <param name="b">Color b.</param>
    /// <param name="t">Interpolation factor (0 to 1).</param>
    /// <returns>The interpolated color.</returns>
    public static Color Lerp(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * t),
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    /// <summary>
    /// Converts an 8-bit sRGB color channel value (0-255) to a linear float value (0.0-1.0).
    /// </summary>
    public static float SrgbToLinear(byte srgbValue)
    {
        float srgb = srgbValue / 255.0f;

        if (srgb <= 0.04045f)
            return srgb / 12.92f;
        else
            return MathF.Pow((srgb + 0.055f) / 1.055f, 2.4f);
    }
}
