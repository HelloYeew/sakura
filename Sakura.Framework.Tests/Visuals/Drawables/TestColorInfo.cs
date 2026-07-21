// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Drawables;

public partial class TestColorInfo : TestScene
{
    [SetUp]
    public void SetUp() => AddStep("Clear", Clear);

    private static Box centredBox() => new Box
    {
        Anchor = Anchor.Centre,
        Origin = Anchor.Centre,
        Size = new Vector2(400, 300),
    };

    [Test]
    public void TestHorizontal()
    {
        AddStep("Add horizontal gradient", () => Add(new Box
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Size = new Vector2(400, 300),
            ColorInfo = ColorInfo.GradientHorizontal(Color.Red, Color.Blue),
        }));
    }

    [Test]
    public void TestVertical()
    {
        AddStep("Add vertical gradient", () => Add(new Box
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Size = new Vector2(400, 300),
            ColorInfo = ColorInfo.GradientVertical(Color.Yellow, Color.Purple),
        }));
    }

    [Test]
    public void TestFourCorners()
    {
        AddStep("Add four-corner gradient", () => Add(new Box
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Size = new Vector2(400, 300),
            ColorInfo = new ColorInfo(Color.Red, Color.Lime, Color.Blue, Color.Yellow),
        }));
    }

    [Test]
    public void TestAlphaGradient()
    {
        AddStep("Add background", () => Add(new Box
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Size = new Vector2(400, 300),
            Color = Color.White,
        }));

        AddStep("Add opaque→transparent overlay", () => Add(new Box
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Size = new Vector2(400, 300),
            ColorInfo = ColorInfo.GradientHorizontal(Color.Red, Color.Red.WithAlpha((byte)0)),
        }));
    }

    [Test]
    public void TestGradientSurvivesFade()
    {
        Box box = null!;

        AddStep("Add gradient box", () => Add(box = new Box
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Size = new Vector2(400, 300),
            ColorInfo = ColorInfo.GradientHorizontal(Color.Cyan, Color.Magenta),
        }));

        AddStep("Loop fade out/in", () => box.FadeOut(1000).Then().FadeIn(1000).Loop());
        AddStep("Fade out (instant check)", () => box.FadeTo(0.4f, 500));
        AddStep("Fade in (instant check)", () => box.FadeTo(1f, 500));
    }

    [Test]
    public void TestInteractive()
    {
        Box box = null!;

        AddStep("Add box", () => Add(box = centredBox()));

        AddStep("Solid red", () => box.ColorInfo = Color.Red);
        AddStep("Horizontal (red -> blue)", () => box.ColorInfo = ColorInfo.GradientHorizontal(Color.Red, Color.Blue));
        AddStep("Vertical (yellow -> purple)", () => box.ColorInfo = ColorInfo.GradientVertical(Color.Yellow, Color.Purple));
        AddStep("Four corners", () => box.ColorInfo = new ColorInfo(Color.Red, Color.Lime, Color.Blue, Color.Yellow));

        AddSliderStep("Alpha", 0f, 1f, 1f, alpha =>
        {
            if (box != null)
                box.Alpha = alpha;
        });
    }
}
