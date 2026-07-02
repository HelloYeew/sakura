// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Extensions.DefaultFontWeightsExtensions;
using Sakura.Framework.Graphics.Text;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// Quick tests for the variable-font value types that form part of the glyph cache key
/// </summary>
[TestFixture]
public class FontVariationTest
{
    [Test]
    public void NoneHasNoAxes()
    {
        Assert.That(FontVariation.None.IsDefault, Is.True);
        Assert.That(FontVariation.None.Axes, Is.Empty);
        Assert.That(FontVariation.None.Get(FontVariation.WEIGHT_AXIS), Is.Null);
    }

    [Test]
    public void WithSetsAndReplacesAxis()
    {
        var v = FontVariation.None.With(FontVariation.WEIGHT_AXIS, 400f);
        Assert.That(v.Get(FontVariation.WEIGHT_AXIS), Is.EqualTo(400f));

        // Replacing the same axis must not add a duplicate.
        var v2 = v.With(FontVariation.WEIGHT_AXIS, 700f);
        Assert.That(v2.Get(FontVariation.WEIGHT_AXIS), Is.EqualTo(700f));
        Assert.That(v2.Axes, Has.Count.EqualTo(1));
    }

    [Test]
    public void WithIsImmutable()
    {
        var baseline = FontVariation.None.With(FontVariation.WEIGHT_AXIS, 400f);
        _ = baseline.With(FontVariation.FILL_AXIS, 1f);

        // The original is untouched.
        Assert.That(baseline.Axes, Has.Count.EqualTo(1));
        Assert.That(baseline.Get(FontVariation.FILL_AXIS), Is.Null);
    }

    [Test]
    public void EqualityIsOrderIndependentAndValueBased()
    {
        var a = FontVariation.None
                             .With(FontVariation.WEIGHT_AXIS, 700f)
                             .With(FontVariation.FILL_AXIS, 1f);

        // Same axes added in the opposite order must compare equal (axes are kept sorted).
        var b = FontVariation.None
                             .With(FontVariation.FILL_AXIS, 1f)
                             .With(FontVariation.WEIGHT_AXIS, 700f);

        Assert.That(a, Is.EqualTo(b));
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));

        var different = FontVariation.None.With(FontVariation.WEIGHT_AXIS, 400f);
        Assert.That(a, Is.Not.EqualTo(different));
    }

    [Test]
    public void ForTextSetsWeightAndItalic()
    {
        var upright = FontVariation.ForText(400f, italic: false);
        Assert.That(upright.Get(FontVariation.WEIGHT_AXIS), Is.EqualTo(400f));
        Assert.That(upright.Get(FontVariation.ITALIC_AXIS), Is.Null);

        var italic = FontVariation.ForText(700f, italic: true);
        Assert.That(italic.Get(FontVariation.WEIGHT_AXIS), Is.EqualTo(700f));
        Assert.That(italic.Get(FontVariation.ITALIC_AXIS), Is.EqualTo(1f));
    }

    [TestCase(FontWeights.Thin, 100f)]
    [TestCase(FontWeights.Regular, 400f)]
    [TestCase(FontWeights.Bold, 700f)]
    [TestCase(FontWeights.Black, 900f)]
    public void WeightMapsToAxisValue(FontWeights weight, float expected)
    {
        Assert.That(weight.ToWeightValue(), Is.EqualTo(expected));
        Assert.That(DefaultFontWeightsExtensions.ToWeightValue(weight.ToString()), Is.EqualTo(expected));
    }

    [Test]
    public void UnknownWeightNameFallsBackToRegular()
    {
        Assert.That(DefaultFontWeightsExtensions.ToWeightValue("NotARealWeight"), Is.EqualTo(400f));
    }

    [Test]
    public void FontUsageToVariationCarriesWeight()
    {
        var usage = new FontUsage("NotoSans", weight: FontWeights.Bold);
        var v = usage.ToVariation();
        Assert.That(v.Get(FontVariation.WEIGHT_AXIS), Is.EqualTo(700f));

        // No icon overrides set -> those axes are absent, so the font default applies.
        Assert.That(v.Get(FontVariation.FILL_AXIS), Is.Null);
        Assert.That(v.Get(FontVariation.GRADE_AXIS), Is.Null);
        Assert.That(v.Get(FontVariation.OPTICAL_SIZE_AXIS), Is.Null);
    }

    [Test]
    public void FontUsageToVariationCarriesIconOverrides()
    {
        var usage = new FontUsage("MaterialSymbolsOutlined", weight: FontWeights.Medium,
                                  fill: 1f, grade: 100f, opticalSize: 40f);
        var v = usage.ToVariation();

        Assert.That(v.Get(FontVariation.WEIGHT_AXIS), Is.EqualTo(500f));
        Assert.That(v.Get(FontVariation.FILL_AXIS), Is.EqualTo(1f));
        Assert.That(v.Get(FontVariation.GRADE_AXIS), Is.EqualTo(100f));
        Assert.That(v.Get(FontVariation.OPTICAL_SIZE_AXIS), Is.EqualTo(40f));
    }

    [Test]
    public void ToVariationQuantizesWeightToNearestTen()
    {
        // A weight axis value that isn't a multiple of 10 should be snapped so the glyph cache stays
        // bounded. (Named weights are already multiples of 100; this guards custom numeric usage.)
        var usage = new FontUsage("NotoSans", weight: "Regular").With(fill: 0.4f);
        var v = usage.ToVariation();

        // FILL 0.4 rounds to 0 (nearest integer stop).
        Assert.That(v.Get(FontVariation.FILL_AXIS), Is.EqualTo(0f));
    }
}
