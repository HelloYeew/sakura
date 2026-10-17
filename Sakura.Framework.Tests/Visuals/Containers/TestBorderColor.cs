// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Containers;

/// <summary>
/// A ring's border:
/// - recoloured with <see cref="TransformExtensions.FadeBorderColorTo{T}"/>, it fades smoothly;
/// - recoloured while out of its mask, it comes back in the new color straight away (a ring whose color
///   was copied over in an update flashed its old color, or white, as it came back);
/// - in front of an additive flash (a clicked button's), it keeps its color; it used to be drawn
///   additively too and light up white until the flash faded.
/// </summary>
public partial class TestBorderColor : TestScene
{
    private Container holder = null!;
    private Box flash = null!;
    private CircularContainer ring = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("a ring in a masked holder", () =>
        {
            Clear();
            Add(holder = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(300),
                Masking = true,
                Children = new Drawable[]
                {
                    flash = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Color = Color.White,
                        Alpha = 0,
                        Blending = BlendingMode.Additive
                    },
                    ring = new CircularContainer
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(160),
                        BorderThickness = 10,
                        BorderColor = Color.SlateGray,
                        // Nothing drawn inside, as the clock's face, so nothing inside sets the blending.
                        Child = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Alpha = 0,
                            AlwaysPresent = true
                        }
                    }
                }
            });
        });
    }

    [Test]
    public void TestFade()
    {
        AddStep("fade to coral", () => ring.FadeBorderColorTo(Color.Coral, 600, Easing.OutQuint));
        AddUntilStep("coral", () => ring.BorderColor.ToArgb() == Color.Coral.ToArgb());

        AddStep("fade to sky blue", () => ring.FadeBorderColorTo(Color.SkyBlue, 600, Easing.OutQuint));
        AddUntilStep("sky blue", () => ring.BorderColor.ToArgb() == Color.SkyBlue.ToArgb());
    }

    [Test]
    public void TestFlashBehindKeepsTheRingsColor()
    {
        AddStep("a dark ring", () => ring.BorderColor = Color.DarkSlateBlue);
        AddStep("flash behind it", () =>
        {
            flash.ClearTransforms();
            flash.Alpha = 0.4f;
            flash.FadeOut(800, Easing.OutQuint);
        });
        AddAssert("the ring is still dark blue (look: not lit white)", () => ring.BorderColor.ToArgb() == Color.DarkSlateBlue.ToArgb());
        AddUntilStep("the flash fades", () => flash.Alpha < 0.01f);
    }

    [Test]
    public void TestRecolourWhileOutOfSight()
    {
        // Placed rather than slid: a masked-away drawable doesn't run its own transforms, so it couldn't
        // slide itself back (the footer slides, and the clock inside it is carried out and back).
        AddStep("out of the holder", () => ring.Y = 400);
        AddUntilStep("out of sight", () => ring.IsMaskedAway);

        AddStep("recolour to gold while away", () => ring.FadeBorderColorTo(Color.Gold, 250, Easing.OutQuint));
        AddWaitStep("let the fade's time pass", 400);

        AddStep("back in", () => ring.Y = 0);
        AddUntilStep("in sight again", () => !ring.IsMaskedAway);
        AddAssert("gold from its first frame back", () => ring.BorderColor.ToArgb() == Color.Gold.ToArgb());
    }
}
