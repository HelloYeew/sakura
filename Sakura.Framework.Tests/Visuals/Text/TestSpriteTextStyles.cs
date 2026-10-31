// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Text;

/// <summary>
/// How text can look without being more than one drawable: spaced out, in a gradient, in colors that run across its letters,
/// with a shadow, and translucent by its color alone.
/// </summary>
public partial class TestSpriteTextStyles : TestScene
{
    private static readonly Color[] rainbow =
    {
        ColorExtensions.FromHex("#D9637A"),
        ColorExtensions.FromHex("#C99A2E"),
        ColorExtensions.FromHex("#7E9B3E"),
        ColorExtensions.FromHex("#517B92"),
        ColorExtensions.FromHex("#B9488E"),
        ColorExtensions.FromHex("#D9637A")
    };

    private SpriteText text = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("Show text", () =>
        {
            Clear();

            Add(new Box { RelativeSizeAxes = Axes.Both, Color = ColorExtensions.FromHex("#E8E4EE") });
            Add(text = new SpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Text = "Full Bloom",
                Font = FontUsage.Default.With(size: 96),
                Color = ColorExtensions.FromHex("#3A2236")
            });
        });
    }

    /// <summary>
    /// Room between its letters, or less of it, keeping it centered.
    /// </summary>
    [Test]
    public void TestLetterSpacing() => AddSliderStep("Letter spacing", -10f, 40f, 0f, spacing => text.LetterSpacing = spacing);

    [Test]
    public void TestGradient()
    {
        AddStep("Left to right", () => text.ColorInfo = ColorInfo.GradientHorizontal(ColorExtensions.FromHex("#E55F9A"), ColorExtensions.FromHex("#517B92")));
        AddStep("Top to bottom", () => text.ColorInfo = ColorInfo.GradientVertical(ColorExtensions.FromHex("#FFD27F"), ColorExtensions.FromHex("#D9637A")));
        AddStep("All four corners", () => text.ColorInfo = new ColorInfo(Color.Red, Color.Lime, Color.Blue, Color.Yellow));
        AddSliderStep("Letter spacing", -10f, 40f, 0f, spacing => text.LetterSpacing = spacing);
        AddSliderStep("Fade", 0f, 1f, 1f, alpha => text.Alpha = alpha);
        AddStep("One color again", () => text.Color = ColorExtensions.FromHex("#3A2236"));
    }

    /// <summary>
    /// A rainbow running along the word, its colors changing across each letter rather than from one to the next.
    /// </summary>
    [Test]
    public void TestColorsThatMove()
    {
        AddStep("A rainbow", () => Add(new Mover(text)));
        AddSliderStep("Fade", 0f, 1f, 1f, alpha => text.Alpha = alpha);
        AddSliderStep("Letter spacing", -10f, 40f, 0f, spacing => text.LetterSpacing = spacing);
    }

    [Test]
    public void TestShadow()
    {
        AddStep("Show a shadow", () => text.Shadow = true);
        AddSliderStep("Across", -20f, 20f, 0f, x => text.ShadowOffset = new Vector2(x, text.ShadowOffset.Y));
        AddSliderStep("Down", -20f, 20f, 2f, y => text.ShadowOffset = new Vector2(text.ShadowOffset.X, y));
        AddSliderStep("Its alpha", 0f, 1f, 0.5f, alpha => text.ShadowColor = Color.Black.WithAlpha(alpha));
        AddSliderStep("Rotation of the text", 0f, 360f, 0f, rotation => text.Rotation = rotation);
        AddSliderStep("Fade", 0f, 1f, 1f, alpha => text.Alpha = alpha);
        AddStep("Put it away", () => text.Shadow = false);
    }

    /// <summary>
    /// A color's own alpha counts on text, as on anything else, on top of a fade.
    /// </summary>
    [Test]
    public void TestAlphaInItsColor()
    {
        AddSliderStep("Its color's alpha", 0f, 1f, 0.5f, alpha => text.Color = ColorExtensions.FromHex("#3A2236").WithAlpha(alpha));
        AddSliderStep("Fade", 0f, 1f, 1f, alpha => text.Alpha = alpha);
    }

    private partial class Mover : Container
    {
        private readonly SpriteText text;

        public Mover(SpriteText text)
        {
            this.text = text;
            text.ColorAt = colorAt;
        }

        private Color colorAt(Vector2 at)
        {
            double along = (at.X / Math.Max(1, text.ContentSize.X) / 3 + Clock.CurrentTime / 2500) % 1;
            double scaled = along * (rainbow.Length - 1);
            int stop = Math.Min((int)scaled, rainbow.Length - 2);

            return rainbow[stop].Blend(rainbow[stop + 1], (float)(scaled - stop));
        }

        public override void Update()
        {
            base.Update();
            text.Invalidate(InvalidationFlags.Color);
        }
    }
}
