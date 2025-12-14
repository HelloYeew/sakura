// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Graphics.Text;

/// <summary>
/// Represents a request for a specific font style and size.
/// </summary>
public readonly struct FontUsage : IEquatable<FontUsage>
{
    public string Family { get; }
    public float Size { get; }
    public string Weight { get; }
    public bool Italics { get; }

    /// <summary>
    /// Gets the default font usage (NotoSans-Regular, 24px).
    /// </summary>
    public static FontUsage Default => new FontUsage("NotoSans", 24);

    public FontUsage(string family = "NotoSans", float size = 24, string weight = "Regular", bool italics = false)
    {
        Family = family;
        Size = size;
        Weight = weight;
        Italics = italics;
    }

    public FontUsage With(string? family = null, float? size = null, string? weight = null, bool? italics = null)
    {
        return new FontUsage(
            family ?? Family,
            size ?? Size,
            weight ?? Weight,
            italics ?? Italics
        );
    }

    public override string ToString() => $"{Family}-{Weight} (Size: {Size}, Italics: {Italics})";

    public bool Equals(FontUsage other)
    {
        return Family == other.Family &&
               Math.Abs(Size - other.Size) < 0.001f &&
               Weight == other.Weight &&
               Italics == other.Italics;
    }

    public override bool Equals(object? obj) => obj is FontUsage other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Family, Size, Weight, Italics);

    public static bool operator ==(FontUsage left, FontUsage right) => left.Equals(right);

    public static bool operator !=(FontUsage left, FontUsage right) => !left.Equals(right);
}
