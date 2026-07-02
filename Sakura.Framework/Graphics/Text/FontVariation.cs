// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;

namespace Sakura.Framework.Graphics.Text;

/// <summary>
/// An immutable set of OpenType variation-axis coordinates (tag -> value) describing a single
/// instance of a variable font e.g. <c>wght=700</c>, or <c>wght=400, FILL=1</c> for a filled
/// Material Symbols icon.
/// </summary>
/// <remarks>
/// The model is deliberately general rather than hard-coded to weight/italic: text fonts vary on
/// <c>wght</c>/<c>ital</c>/<c>slnt</c>, while icon fonts (Material Symbols) vary on
/// <c>wght</c>/<c>FILL</c>/<c>GRAD</c>/<c>opsz</c>. Only axes the loaded face actually exposes are
/// applied; unknown axes are ignored and values are clamped to each axis' range.
/// </remarks>
public readonly struct FontVariation : IEquatable<FontVariation>
{
    public static readonly uint WEIGHT_AXIS = FreeTypeVariations.Tag("wght");
    public static readonly uint ITALIC_AXIS = FreeTypeVariations.Tag("ital");
    public static readonly uint SLANT_AXIS = FreeTypeVariations.Tag("slnt");
    public static readonly uint WIDTH_AXIS = FreeTypeVariations.Tag("wdth");
    public static readonly uint FILL_AXIS = FreeTypeVariations.Tag("FILL");
    public static readonly uint GRADE_AXIS = FreeTypeVariations.Tag("GRAD");
    public static readonly uint OPTICAL_SIZE_AXIS = FreeTypeVariations.Tag("opsz");

    private readonly (uint Tag, float Value)[]? axes;

    private static readonly (uint Tag, float Value)[] empty = Array.Empty<(uint, float)>();

    /// <summary>
    /// The axis coordinates, sorted by tag. Empty when this is <see cref="None"/>.
    /// </summary>
    public IReadOnlyList<(uint Tag, float Value)> Axes => axes ?? empty;

    /// <summary>
    /// True when no axis overrides are set (the font's default instance should be used).
    /// </summary>
    public bool IsDefault => axes == null || axes.Length == 0;

    private FontVariation((uint Tag, float Value)[]? axes)
    {
        this.axes = axes;
    }

    /// <summary>
    /// No axis overrides, render the font's default instance.
    /// </summary>
    public static FontVariation None => default;

    /// <summary>
    /// Returns a new <see cref="FontVariation"/> with <paramref name="tag"/> set to
    /// <paramref name="value"/>, replacing any existing value for that axis.
    /// </summary>
    public FontVariation With(uint tag, float value)
    {
        var current = axes ?? empty;

        var list = new List<(uint Tag, float Value)>(current.Length + 1);
        bool replaced = false;
        foreach (var a in current)
        {
            if (a.Tag == tag)
            {
                list.Add((tag, value));
                replaced = true;
            }
            else
            {
                list.Add(a);
            }
        }

        if (!replaced)
            list.Add((tag, value));

        var result = list.ToArray();
        Array.Sort(result, static (x, y) => x.Tag.CompareTo(y.Tag));
        return new FontVariation(result);
    }

    /// <summary>
    /// Gets the value for <paramref name="tag"/>, or null if this variation doesn't set it.
    /// </summary>
    public float? Get(uint tag)
    {
        var current = axes;
        if (current == null) return null;

        foreach (var a in current)
        {
            if (a.Tag == tag)
                return a.Value;
        }

        return null;
    }

    /// <summary>
    /// Convenience for the common text case: sets the <c>wght</c> axis and, when
    /// <paramref name="italic"/> is requested, the <c>ital</c> axis (applied only if the face exposes
    /// it; faces that ship italic as a separate file simply ignore it).
    /// </summary>
    public static FontVariation ForText(float weight, bool italic)
    {
        var v = None.With(WEIGHT_AXIS, weight);
        if (italic)
            v = v.With(ITALIC_AXIS, 1f);
        return v;
    }

    public bool Equals(FontVariation other)
    {
        var a = axes ?? empty;
        var b = other.axes ?? empty;

        if (a.Length != b.Length) return false;

        for (int i = 0; i < a.Length; i++)
        {
            if (a[i].Tag != b[i].Tag || Math.Abs(a[i].Value - b[i].Value) > 0.001f)
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is FontVariation other && Equals(other);

    public override int GetHashCode()
    {
        var current = axes;
        if (current == null || current.Length == 0) return 0;

        var hash = new HashCode();
        foreach (var a in current)
        {
            hash.Add(a.Tag);
            // Quantize the value into the hash so near-equal floats (that Equals treats as equal)
            // don't land in different buckets.
            hash.Add((int)MathF.Round(a.Value * 100f));
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(FontVariation left, FontVariation right) => left.Equals(right);

    public static bool operator !=(FontVariation left, FontVariation right) => !left.Equals(right);

    public override string ToString()
    {
        var current = axes;
        if (current == null || current.Length == 0) return "default";

        string[] parts = new string[current.Length];
        for (int i = 0; i < current.Length; i++)
        {
            uint tag = current[i].Tag;
            string tagStr = new string(new[]
            {
                (char)((tag >> 24) & 0xFF), (char)((tag >> 16) & 0xFF),
                (char)((tag >> 8) & 0xFF), (char)(tag & 0xFF)
            });
            parts[i] = $"{tagStr}={current[i].Value:0.##}";
        }

        return string.Join(", ", parts);
    }
}
