// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;

namespace Sakura.Framework.Tests.Graphics;

[TestFixture]
public class OklabColorTest
{
    private static readonly Color[] samples =
    {
        Color.Black,
        Color.White,
        ColorExtensions.FromHex("#88C0D0"),
        ColorExtensions.FromHex("#EBCB8B"),
        ColorExtensions.FromHex("#D08770"),
        Color.FromArgb(255, 0, 0),
        Color.FromArgb(0, 0, 255)
    };

    [Test]
    public void ColorsSurviveTheRoundTrip()
    {
        using (Assert.EnterMultipleScope())
        {
            // Compared by channel: a named color (Color.Black) isn't equal to the same color built from numbers.
            foreach (var color in samples)
                Assert.That(channels(OklabColor.FromColor(color).ToColor()), Is.EqualTo(channels(color)), $"#{color.R:X2}{color.G:X2}{color.B:X2}");
        }
    }

    [Test]
    public void GreysHaveNoColor()
    {
        var white = OklabColor.FromColor(Color.White);
        var black = OklabColor.FromColor(Color.Black);
        var grey = OklabColor.FromColor(Color.FromArgb(128, 128, 128));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(white.L, Is.EqualTo(1).Within(1e-4));
            Assert.That(black.L, Is.EqualTo(0).Within(1e-4));
            Assert.That(white.Chroma, Is.LessThan(1e-4));
            Assert.That(grey.Chroma, Is.LessThan(1e-4));
        }
    }

    [Test]
    public void APerceptualBlendKeepsItsEnds()
    {
        var from = ColorExtensions.FromHex("#88C0D0");
        var to = ColorExtensions.FromHex("#EBCB8B");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(from.BlendPerceptually(to, 0), Is.EqualTo(from));
            Assert.That(from.BlendPerceptually(to, 1), Is.EqualTo(to));
            Assert.That(from.BlendPerceptually(to, -1), Is.EqualTo(from), "clamped");
            Assert.That(from.BlendPerceptually(to, 2), Is.EqualTo(to), "clamped");
        }
    }

    /// <summary>
    /// What it's for: halfway is halfway in how light it looks. Mixing the RGB numbers of black and white
    /// gives a grey that looks well over halfway to white.
    /// </summary>
    [Test]
    public void HalfwayLooksHalfway()
    {
        var middle = OklabColor.FromColor(Color.Black.BlendPerceptually(Color.White, 0.5f));
        var rgbMiddle = OklabColor.FromColor(Color.Black.Blend(Color.White, 0.5f));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(middle.L, Is.EqualTo(0.5).Within(0.01));
            Assert.That(rgbMiddle.L, Is.GreaterThan(0.55), "the plain blend, for comparison");
        }
    }

    [Test]
    public void AlphaBlendsAsItDoesInBlend()
    {
        var clear = Color.FromArgb(0, 255, 0, 0);
        var solid = Color.FromArgb(255, 0, 0, 255);

        Assert.That(clear.BlendPerceptually(solid, 0.5f).A, Is.EqualTo(128).Within(1));
    }

    [Test]
    public void ChromaIsTheDistanceFromGrey() =>
        Assert.That(new OklabColor(0.5, 0.3, 0.4).Chroma, Is.EqualTo(0.5).Within(1e-9));

    private static (byte A, byte R, byte G, byte B) channels(Color color) => (color.A, color.R, color.G, color.B);
}
