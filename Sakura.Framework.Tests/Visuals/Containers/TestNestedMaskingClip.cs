// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Containers;

public partial class TestNestedMaskingClip : TestScene
{
    private const float panel_width = 260;
    private const float panel_height = 380;

    [SetUp]
    public void SetUp()
    {
        AddStep("Clear screen", Clear);

        AddStep("Add leak-detector backdrop", () => Add(new Box
        {
            RelativeSizeAxes = Axes.Both,
            Color = Color.Magenta
        }));
    }

    /// <summary>
    /// App structure from real situation
    /// </summary>
    [Test]
    public void TestColorPickerInScrollPanel()
    {
        ScrollableContainer scroll = null!;

        AddStep("Add panel with scrolling color picker", () =>
        {
            Add(new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(panel_width, panel_height),
                Masking = true, // clip any content that overflows the panel bounds
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Alpha = 0.6f,
                        Color = Color.Black
                    },
                    scroll = new ScrollableContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        Direction = ScrollDirection.Vertical,
                        AutoHideScrollbars = true,
                        Child = new FlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FlowDirection.Vertical,
                            Spacing = new Vector2(6, 6),
                            Padding = new MarginPadding(15),
                            Children = new Drawable[]
                            {
                                filler("Adjustments", Color.Orange),
                                filler("Row A", Color.White),
                                filler("Row B", Color.White),
                                filler("Row C", Color.White),
                                filler("Row D", Color.White),
                                filler("Row E", Color.White),
                                filler("Overlay colors", Color.Orange),
                                new BasicColorPicker
                                {
                                    SaturationValueAreaSize = new Vector2(220, 130)
                                }
                            }
                        }
                    }
                }
            });
        });

        AddUntilStep("wait for layout", () => scroll.ScrollableExtent.Y > 0);

        AddStep("Scroll to end (picker fully in view)", () => scroll.ScrollToEnd(animated: false));

        AddSliderStep("Scroll position", 0f, 1f, 1f, v =>
        {
            if (scroll != null)
                scroll.ScrollTo(new Vector2(0, v * scroll.ScrollableExtent.Y), animated: false);
        });
    }

    [Test]
    public void TestRoundedChildStraddlesOuterEdge()
    {
        Container roundedChild = null!;
        Container panel = null!;

        AddStep("Add panel with rounded child", () =>
        {
            Add(panel = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(panel_width, panel_height),
                Masking = true,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Alpha = 0.6f,
                        Color = Color.Black
                    },
                    roundedChild = new Container
                    {
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.TopLeft,
                        Size = new Vector2(220, 160),
                        Masking = true,
                        CornerRadius = 8,
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Color = Color.LimeGreen
                            }
                        }
                    }
                }
            });
        });

        AddSliderStep("Child Y offset", 0f, panel_height + 40, panel_height - 120, y =>
        {
            if (roundedChild != null)
                roundedChild.Y = y;
        });

        AddStep("Park child half past bottom edge", () =>
        {
            if (roundedChild != null)
                roundedChild.Y = panel_height - 60;
        });
    }

    [Test]
    public void TestBorderLeaksPastAncestorMask()
    {
        Container borderedChild = null!;

        AddStep("Add panel with bordered child", () =>
        {
            Add(new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(panel_width, panel_height),
                Masking = true,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Alpha = 0.6f,
                        Color = Color.Black
                    },
                    borderedChild = new Container
                    {
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.TopLeft,
                        Size = new Vector2(200, 120),
                        Masking = true,
                        CornerRadius = 12,
                        BorderThickness = 6,
                        BorderColor = Color.White,
                        Child = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Color = Color.DarkGreen
                        }
                    }
                }
            });
        });

        AddSliderStep("Child Y offset", 0f, panel_height + 40, panel_height - 150, y =>
        {
            if (borderedChild != null)
                borderedChild.Y = y;
        });

        AddStep("Park child straddling bottom edge", () =>
        {
            if (borderedChild != null)
                borderedChild.Y = panel_height - 40;
        });
    }

    private static Container filler(string text, Color color) => new Container
    {
        RelativeSizeAxes = Axes.X,
        Height = 40,
        Children = new Drawable[]
        {
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Alpha = 0.25f,
                Color = Color.White
            },
            new SpriteText
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Margin = new MarginPadding { Left = 8 },
                Text = text,
                Color = color
            }
        }
    };
}
