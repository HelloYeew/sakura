// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Extensions.DefaultFontWeightsExtensions;

namespace Sakura.Framework.Graphics.Text;

/// <summary>
/// Represents a request for a specific font style and size.
/// </summary>
public readonly struct FontUsage : IEquatable<FontUsage>
{
    public const float DEFAULT_FONT_SIZE = 24;
    public const string DEFAULT_FONT_FAMILY = "NotoSans";

    public string Family { get; }
    public float Size { get; }
    public string Weight { get; }
    public bool Italics { get; }

    /// <summary>
    /// Optional override for the <c>FILL</c> variation axis (0 = outlined, 1 = filled). Only meaningful
    /// for variable fonts that expose the axis (e.g. Material Symbols); null leaves the font default.
    /// </summary>
    public float? Fill { get; }

    /// <summary>
    /// Optional override for the <c>GRAD</c> (grade) variation axis. Fine emphasis adjustment,
    /// typically −50…200 for Material Symbols. Null leaves the font default.
    /// </summary>
    public float? Grade { get; }

    /// <summary>
    /// Optional override for the <c>opsz</c> (optical size) variation axis. Null leaves the font
    /// default (or lets a consumer such as <see cref="Drawables.IconSprite"/> auto-derive it from the
    /// render size).
    /// </summary>
    public float? OpticalSize { get; }

    /// <summary>
    /// Gets the default font usage (NotoSans-Regular, 24px).
    /// </summary>
    public static FontUsage Default => new FontUsage("NotoSans", weight: FontWeights.Regular);

    /// <summary>
    /// Creates a font usage. <paramref name="weight"/> accepts either a <see cref="FontWeights"/>
    /// value or a weight-name string (via <see cref="FontWeight"/>'s implicit conversions), so both
    /// <c>weight: DefaultFontWeights.Bold</c> and <c>weight: "Bold"</c> work through this single
    /// constructor without overload ambiguity.
    /// </summary>
    public FontUsage(string family = DEFAULT_FONT_FAMILY, float size = DEFAULT_FONT_SIZE, FontWeight weight = default, bool italics = false,
                     float? fill = null, float? grade = null, float? opticalSize = null)
    {
        Family = family;
        Size = size;
        Weight = weight.Name; // FontWeight.Name is "Regular" when unset (default)
        Italics = italics;
        Fill = fill;
        Grade = grade;
        OpticalSize = opticalSize;
    }

    public FontUsage With(string? family = null, float? size = null, FontWeight? weight = null, bool? italics = null,
                          float? fill = null, float? grade = null, float? opticalSize = null)
    {
        return new FontUsage(
            family ?? Family,
            size ?? Size,
            weight?.Name ?? Weight,
            italics ?? Italics,
            fill ?? Fill,
            grade ?? Grade,
            opticalSize ?? OpticalSize
        );
    }

    /// <summary>
    /// Builds the <see cref="FontVariation"/> requested by this usage: the <c>wght</c> axis derived
    /// from <see cref="Weight"/>, plus any <see cref="Fill"/>/<see cref="Grade"/>/<see cref="OpticalSize"/>
    /// overrides. Axis values are quantized so arbitrary requests don't grow the glyph cache
    /// unbounded. Axes the resolved face doesn't expose are ignored by <see cref="Font"/>.
    /// </summary>
    public FontVariation ToVariation()
    {
        // wght is quantized to the nearest 10; the discrete-stop axes to the nearest integer.
        float weightValue = MathF.Round(DefaultFontWeightsExtensions.ToWeightValue(Weight) / 10f) * 10f;

        var variation = FontVariation.None.With(FontVariation.WEIGHT_AXIS, weightValue);

        if (Fill.HasValue)
            variation = variation.With(FontVariation.FILL_AXIS, MathF.Round(Fill.Value));

        if (Grade.HasValue)
            variation = variation.With(FontVariation.GRADE_AXIS, MathF.Round(Grade.Value));

        if (OpticalSize.HasValue)
            variation = variation.With(FontVariation.OPTICAL_SIZE_AXIS, MathF.Round(OpticalSize.Value));

        return variation;
    }

    public override string ToString()
    {
        string s = $"{Family}-{Weight} (Size: {Size}, Italics: {Italics}";
        if (Fill.HasValue) s += $", Fill: {Fill}";
        if (Grade.HasValue) s += $", Grade: {Grade}";
        if (OpticalSize.HasValue) s += $", opsz: {OpticalSize}";
        return s + ")";
    }

    public bool Equals(FontUsage other)
    {
        return Family == other.Family &&
               Math.Abs(Size - other.Size) < 0.001f &&
               Weight == other.Weight &&
               Italics == other.Italics &&
               Nullable.Equals(Fill, other.Fill) &&
               Nullable.Equals(Grade, other.Grade) &&
               Nullable.Equals(OpticalSize, other.OpticalSize);
    }

    public override bool Equals(object? obj) => obj is FontUsage other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Family, Size, Weight, Italics, Fill, Grade, OpticalSize);

    public static bool operator ==(FontUsage left, FontUsage right) => left.Equals(right);

    public static bool operator !=(FontUsage left, FontUsage right) => !left.Equals(right);
}
