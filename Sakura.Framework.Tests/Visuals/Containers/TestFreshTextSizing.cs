// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Containers;

/// <summary>
/// Things that size themselves around text, with no waiting for the text to be measured: a footer-style
/// name tab (held in from the right, eased, never narrower than 100), and a count badge whose number
/// changes while it's hidden. Type in the box to rename the tab.
/// </summary>
public partial class TestFreshTextSizing : TestScene
{
    private const float min_tab_width = 100;
    private const float right_gap = 16;
    private const float left_room = 40;

    private Container tab = null!;
    private SpriteText tabText = null!;
    private Container badge = null!;
    private SpriteText badgeText = null!;
    private BasicTextBox nameBox = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("Build", () =>
        {
            Clear();

            Add(tab = new Container
            {
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                Position = new Vector2(-40, 40),
                Height = 52,
                AutoSizeAxes = Axes.X,
                AutoSizeMinimum = new Vector2(min_tab_width, 0),
                AutoSizeDuration = 250,
                AutoSizeEasing = Easing.OutQuint,
                Padding = new MarginPadding { Left = left_room },
                Children = new Drawable[]
                {
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        CornerRadius = 6,
                        Shear = new Vector2(-0.5f, 0),
                        Child = new Box { RelativeSizeAxes = Axes.Both, Color = Color.SlateBlue }
                    },
                    tabText = new SpriteText
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        X = -right_gap,
                        Text = "Main Menu",
                        Color = Color.White
                    }
                }
            });

            Add(badge = new Container
            {
                Position = new Vector2(40, 120),
                AutoSizeAxes = Axes.Both,
                AutoSizeMinimum = new Vector2(18, 18),
                Padding = new MarginPadding { Left = 5, Right = 5 },
                Alpha = 0,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Color = Color.HotPink },
                    badgeText = new SpriteText { Anchor = Anchor.Centre, Origin = Anchor.Centre, Text = "0", Color = Color.White }
                }
            });

            Add(nameBox = new BasicTextBox
            {
                Position = new Vector2(40, 40),
                Size = new Vector2(300, 36),
                PlaceholderText = "Screen name..."
            });

            nameBox.Text.ValueChanged += e => tabText.Text = e.NewValue;
        });
    }

    /// <summary>The width the tab heads for: the text, its gap and the room on the left, at least the minimum.</summary>
    private float tabTarget => System.Math.Max(min_tab_width, tabText.Width + right_gap + left_room);

    [Test]
    public void TestTabFollowsItsText()
    {
        AddStep("A long name", () => tabText.Text = "Edit: a very long beatmap name");
        AddAssert("Text width known straight away", () => tabText.DrawSize.X == tabText.Width);
        AddUntilStep("Tab eases to fit", () => System.Math.Abs(tab.Width - tabTarget) < 0.5f);

        AddStep("An empty name", () => tabText.Text = string.Empty);
        AddUntilStep("Never narrower than the minimum", () => System.Math.Abs(tab.Width - min_tab_width) < 0.5f);
    }

    [Test]
    public void TestBadgeSizedWhileHidden()
    {
        AddStep("Count goes up while hidden", () => badgeText.Text = "128");
        AddAssert("Already sized for it", () => badge.Width == System.Math.Max(18, badgeText.Width + 10));
        AddStep("Show it", () => badge.Alpha = 1);
        AddStep("Count back down", () => badgeText.Text = "3");
        AddAssert("Shrinks at once, to the minimum if need be", () => badge.Width == System.Math.Max(18, badgeText.Width + 10));
    }
}
