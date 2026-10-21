// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Containers;

public partial class TestSafeAreaContainer : TestScene
{
    private SafeAreaDefiningContainer screen = null!;
    private Box background = null!;
    private Box content = null!;
    private Box barBackground = null!;
    private SafeAreaContainer barContent = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("Create simulated screen", () =>
        {
            Clear();

            // The black outside the simulated screen is what a bare cutout looks like: nothing drawn there.
            Add(screen = new SafeAreaDefiningContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.None,
                Size = new Vector2(800, 400),
                Children = new Drawable[]
                {
                    background = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Color = Color.DarkRed,
                        Bleed = Edges.All
                    },
                    content = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Size = new Vector2(1, 0.8f),
                        Color = Color.DarkSlateGray
                    },
                    new SpriteText
                    {
                        Text = "Safe content. The red background bleeds under the simulated cutouts.",
                        Position = new Vector2(10, 10),
                        Color = Color.White
                    },
                    new Container
                    {
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.BottomLeft,
                        RelativeSizeAxes = Axes.Both,
                        Size = new Vector2(1, 0.2f),
                        Children = new Drawable[]
                        {
                            barBackground = new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Color = Color.SteelBlue,
                                Bleed = Edges.All
                            },
                            barContent = new SafeAreaContainer
                            {
                                Child = new SpriteText
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Text = "A bar: its colour bleeds, its text stays safe",
                                    Color = Color.White
                                }
                            }
                        }
                    }
                }
            });
        });
    }

    [Test]
    public void TestSimulatedInsets()
    {
        AddStep("Simulate notch (top 60)", () => screen.OverrideSafeArea = new MarginPadding { Top = 60 });
        AddAssert("Content is below the notch", () => screen.Padding.Top > 59 && content.DrawRectangle.Y >= screen.DrawRectangle.Y + 59);
        AddAssert("Background reaches the top edge", () => background.DrawRectangle.Y <= screen.DrawRectangle.Y + 0.5f);

        AddStep("Simulate landscape phone (left 80, bottom 30)", () => screen.OverrideSafeArea = new MarginPadding { Left = 80, Bottom = 30 });
        AddAssert("Bar colour reaches the bottom left corner", () =>
            barBackground.DrawRectangle.X <= screen.DrawRectangle.X + 0.5f
            && barBackground.DrawRectangle.Y + barBackground.DrawRectangle.Height >= screen.DrawRectangle.Y + screen.DrawRectangle.Height - 0.5f);
        AddAssert("Bar content pads nothing (the bar is already safe)", () => barContent.Padding.Equals(new MarginPadding()));

        AddStep("Simulate all edges", () => screen.OverrideSafeArea = new MarginPadding(40));

        AddSliderStep("Top inset", 0f, 150f, 60f, v =>
        {
            if (screen != null)
            {
                var p = screen.OverrideSafeArea ?? new MarginPadding();
                p.Top = v;
                screen.OverrideSafeArea = p;
            }
        });
    }

    [Test]
    public void TestBleedEdges()
    {
        AddStep("Simulate all edges (40)", () => screen.OverrideSafeArea = new MarginPadding(40));
        AddStep("Background bleeds on top only", () => background.Bleed = Edges.Top);
        AddAssert("Only the top reached the edge", () =>
            background.DrawRectangle.Y <= screen.DrawRectangle.Y + 0.5f && background.DrawRectangle.X >= screen.DrawRectangle.X + 39);
        AddStep("Background doesn't bleed", () => background.Bleed = Edges.None);
        AddAssert("Background is inside the safe area", () => background.DrawRectangle.X >= screen.DrawRectangle.X + 39);
        AddStep("Background bleeds on all edges", () => background.Bleed = Edges.All);
    }

    [Test]
    public void TestFollowWindow()
    {
        AddStep("Follow window safe area", () => screen.OverrideSafeArea = null);
        AddAssert("Following the outer safe area", () => screen.OverrideSafeArea == null);
    }
}
