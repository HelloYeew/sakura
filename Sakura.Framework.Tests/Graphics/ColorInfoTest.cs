// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;

namespace Sakura.Framework.Tests.Graphics;

public class ColorInfoTest
{
    [Test]
    public void TestImplicitConversionFromColorIsSolid()
    {
        ColorInfo info = Color.Red;

        Assert.That(info.HasSingleColor, Is.True);
        Assert.That(info.TopLeft, Is.EqualTo(Color.Red));
        Assert.That(info.TopRight, Is.EqualTo(Color.Red));
        Assert.That(info.BottomLeft, Is.EqualTo(Color.Red));
        Assert.That(info.BottomRight, Is.EqualTo(Color.Red));
    }

    [Test]
    public void TestSolid()
    {
        var info = ColorInfo.Solid(Color.Lime);

        Assert.That(info.HasSingleColor, Is.True);
        Assert.That(info.TopLeft, Is.EqualTo(Color.Lime));
    }

    [Test]
    public void TestGradientHorizontal()
    {
        var info = ColorInfo.GradientHorizontal(Color.Red, Color.Blue);

        Assert.That(info.HasSingleColor, Is.False);
        // Left edge (TL, BL) = left color; right edge (TR, BR) = right color
        Assert.That(info.TopLeft, Is.EqualTo(Color.Red));
        Assert.That(info.BottomLeft, Is.EqualTo(Color.Red));
        Assert.That(info.TopRight, Is.EqualTo(Color.Blue));
        Assert.That(info.BottomRight, Is.EqualTo(Color.Blue));
    }

    [Test]
    public void TestGradientVertical()
    {
        var info = ColorInfo.GradientVertical(Color.Red, Color.Blue);

        Assert.That(info.HasSingleColor, Is.False);
        // Top edge (TL, TR) = top color; bottom edge (BL, BR) = bottom color.
        Assert.That(info.TopLeft, Is.EqualTo(Color.Red));
        Assert.That(info.TopRight, Is.EqualTo(Color.Red));
        Assert.That(info.BottomLeft, Is.EqualTo(Color.Blue));
        Assert.That(info.BottomRight, Is.EqualTo(Color.Blue));
    }

    [Test]
    public void TestValueEquality()
    {
        var a = ColorInfo.GradientHorizontal(Color.Red, Color.Blue);
        var b = ColorInfo.GradientHorizontal(Color.Red, Color.Blue);
        var c = ColorInfo.GradientHorizontal(Color.Red, Color.Green);

        Assert.That(a, Is.EqualTo(b));
        Assert.That(a == b, Is.True);
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));

        Assert.That(a, Is.Not.EqualTo(c));
        Assert.That(a != c, Is.True);
    }

    [Test]
    public void TestSolidColorEqualsImplicitConversion()
    {
        ColorInfo fromColor = Color.White;
        var solid = ColorInfo.Solid(Color.White);

        Assert.That(fromColor, Is.EqualTo(solid));
    }

    [Test]
    public void TestMultiplyAlpha()
    {
        var info = ColorInfo.Solid(Color.FromArgb(200, 10, 20, 30)).MultiplyAlpha(0.5f);

        Assert.That(info.TopLeft.A, Is.EqualTo(100));
        // RGB is untouched.
        Assert.That(info.TopLeft.R, Is.EqualTo(10));
        Assert.That(info.TopLeft.G, Is.EqualTo(20));
        Assert.That(info.TopLeft.B, Is.EqualTo(30));
    }

    [Test]
    public void TestMultiplyAlphaClampsToByteRange()
    {
        var info = ColorInfo.Solid(Color.FromArgb(200, 255, 255, 255)).MultiplyAlpha(2f);

        Assert.That(info.TopLeft.A, Is.EqualTo(255));
    }

    [Test]
    public void TestMultiplyAlphaPerCorner()
    {
        var info = new ColorInfo(
            Color.FromArgb(100, 0, 0, 0),
            Color.FromArgb(200, 0, 0, 0),
            Color.FromArgb(50, 0, 0, 0),
            Color.FromArgb(255, 0, 0, 0)).MultiplyAlpha(0.5f);

        Assert.That(info.TopLeft.A, Is.EqualTo(50));
        Assert.That(info.TopRight.A, Is.EqualTo(100));
        Assert.That(info.BottomLeft.A, Is.EqualTo(25));
        Assert.That(info.BottomRight.A, Is.EqualTo(127));
    }

    [Test]
    public void TestLerpEndpoints()
    {
        // Build endpoints via FromArgb so they compare equal to Lerp's FromArgb-reconstructed
        // corners (Color equality is identity-sensitive: a named color != its ARGB twin).
        var red = Color.FromArgb(255, 255, 0, 0);
        var blue = Color.FromArgb(255, 0, 0, 255);
        var a = ColorInfo.GradientHorizontal(red, blue);
        var b = ColorInfo.GradientHorizontal(blue, red);

        Assert.That(ColorInfo.Lerp(a, b, 0f), Is.EqualTo(a));
        Assert.That(ColorInfo.Lerp(a, b, 1f), Is.EqualTo(b));
    }

    [Test]
    public void TestLerpMidpointIsCornerWise()
    {
        var a = ColorInfo.Solid(Color.FromArgb(255, 0, 0, 0));
        var b = ColorInfo.Solid(Color.FromArgb(255, 100, 100, 100));

        var mid = ColorInfo.Lerp(a, b, 0.5f);

        Assert.That(mid.TopLeft.R, Is.EqualTo(50));
        Assert.That(mid.TopLeft.G, Is.EqualTo(50));
        Assert.That(mid.TopLeft.B, Is.EqualTo(50));
    }
}
