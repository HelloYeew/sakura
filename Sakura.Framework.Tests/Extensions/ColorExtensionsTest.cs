// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;

namespace Sakura.Framework.Tests.Extensions;

/// <summary>
/// Relative luminance and contrast ratio, as the WCAG contrast guidelines define them.
/// </summary>
[TestFixture]
public class ColorExtensionsTest
{
    [Test]
    public void LuminanceRunsFromBlackToWhite()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Color.Black.GetRelativeLuminance(), Is.EqualTo(0).Within(1e-6));
            Assert.That(Color.White.GetRelativeLuminance(), Is.EqualTo(1).Within(1e-6));
        }
    }

    /// <summary>
    /// Each primary weighs what the guidelines give it: green looks brightest, blue darkest.
    /// </summary>
    [Test]
    public void EachChannelWeighsAsItLooks()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Color.FromArgb(255, 0, 0).GetRelativeLuminance(), Is.EqualTo(0.2126).Within(1e-4));
            Assert.That(Color.FromArgb(0, 255, 0).GetRelativeLuminance(), Is.EqualTo(0.7152).Within(1e-4));
            Assert.That(Color.FromArgb(0, 0, 255).GetRelativeLuminance(), Is.EqualTo(0.0722).Within(1e-4));
        }
    }

    /// <summary>
    /// The ends, either way round, and a color against itself.
    /// </summary>
    [Test]
    public void ContrastRunsFromOneToTwentyOne()
    {
        var grey = ColorExtensions.FromHex("#88C0D0");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Color.Black.GetContrastRatio(Color.White), Is.EqualTo(21).Within(1e-3));
            Assert.That(Color.White.GetContrastRatio(Color.Black), Is.EqualTo(21).Within(1e-3));
            Assert.That(grey.GetContrastRatio(grey), Is.EqualTo(1));
        }
    }

    /// <summary>
    /// The guidelines' own example: #767676 is the lightest grey whose text reads on white (4.54), #777777 just misses
    /// (4.48).
    /// </summary>
    [Test]
    public void TheLightestGreyThatReadsOnWhite()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ColorExtensions.FromHex("#767676").GetContrastRatio(Color.White), Is.EqualTo(4.54).Within(0.01));
            Assert.That(ColorExtensions.FromHex("#777777").GetContrastRatio(Color.White), Is.EqualTo(4.48).Within(0.01));
        }
    }

    /// <summary>
    /// Brightness and luminance aren't the same measure: a light grey that's 0.71 bright is only 0.46 luminous, as the
    /// channels are weighed before and after linear light.
    /// </summary>
    [Test]
    public void LuminanceIsNotBrightness()
    {
        var surface = ColorExtensions.FromHex("#B4B4C4");

        using (Assert.EnterMultipleScope())
        {
            // Called as a static: Color's own GetBrightness (its HSL lightness, 0.74 here) takes `surface.GetBrightness()`.
            Assert.That(ColorExtensions.GetBrightness(surface), Is.EqualTo(0.71).Within(0.01));
            Assert.That(surface.GetRelativeLuminance(), Is.EqualTo(0.46).Within(0.01));
        }
    }

    /// <summary>
    /// Both colors are taken as opaque: a faded one measures as its full self.
    /// </summary>
    [Test]
    public void AlphaIsIgnored()
    {
        var ink = ColorExtensions.FromHex("#393947");
        var surface = ColorExtensions.FromHex("#B4B4C4");

        Assert.That(ink.WithAlpha(0.5f).GetContrastRatio(surface), Is.EqualTo(ink.GetContrastRatio(surface)));
    }
}
