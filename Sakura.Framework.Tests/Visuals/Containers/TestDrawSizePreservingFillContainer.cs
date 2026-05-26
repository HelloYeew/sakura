// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;
using Sakura.Framework.Utilities;

namespace Sakura.Framework.Tests.Visuals.Containers;

public class TestDrawSizePreservingFillContainer : TestScene
{
    private DrawSizePreservingFillContainer fillContainer = null!;
    private SpriteText strategyText = null!;

    private readonly Vector2 parentSize = new Vector2(1200, 600);
    private readonly Vector2 targetSize = new Vector2(400, 300);

    // expected value
    // X = 1200 / 400 = 3.0
    // Y = 600 / 300 = 2.0

    [SetUp]
    public void SetUp()
    {
        AddStep("Create simulated parent", () =>
        {
            Clear();
            Add(new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = parentSize,
                Masking = true,
                BorderThickness = 4,
                BorderColor = Color.White,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Color = Color.DarkGray
                    },
                    fillContainer = new DrawSizePreservingFillContainer
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        TargetDrawSize = targetSize,
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Color = ColorExtensions.FromRgba(255, 100, 100, 128)
                            },
                            strategyText = new SpriteText
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Text = "Strategy: None",
                                Font = FontUsage.Default.With(size: 30, weight: "Bold")
                            }
                        }
                    }
                }
            });
        });
    }

    [Test]
    public void TestMinimumStrategy()
    {
        AddStep("Set Minimum Strategy", () =>
        {
            fillContainer.Strategy = DrawSizePreservationStrategy.Minimum;
            strategyText.Text = "Strategy: Minimum (Scale = min(3,2) = 2)";
        });

        // min(3.0, 2.0) = 2.0
        // scale = (2.0, 2.0)
        // size = (1 / 2.0, 1 / 2.0) = (0.5, 0.5)
        addWaitStep();
        AddAssert("Scale is 2.0", () => Precision.AlmostEquals(fillContainer.Scale, new Vector2(2f)));
        AddAssert("Size is 0.5", () => Precision.AlmostEquals(fillContainer.Size, new Vector2(0.5f)));
    }

    [Test]
    public void TestMaximumStrategy()
    {
        AddStep("Set Maximum Strategy", () =>
        {
            fillContainer.Strategy = DrawSizePreservationStrategy.Maximum;
            strategyText.Text = "Strategy: Maximum (Scale = max(3,2) = 3)";
        });

        // max(3.0, 2.0) = 3.0
        // scale = (3.0, 3.0)
        // size = (1 / 3.0, 1 / 3.0) -> (0.333, 0.333)
        addWaitStep();
        AddAssert("Scale is 3.0", () => Precision.AlmostEquals(fillContainer.Scale, new Vector2(3f)));
        AddAssert("Size is ~0.333", () => Precision.AlmostEquals(fillContainer.Size, new Vector2(1f / 3f)));
    }

    [Test]
    public void TestAverageStrategy()
    {
        AddStep("Set Average Strategy", () =>
        {
            fillContainer.Strategy = DrawSizePreservationStrategy.Average;
            strategyText.Text = "Strategy: Average (Scale = (3+2)/2 = 2.5)";
        });

        // (3.0 + 2.0) / 2 = 2.5
        // scale = (2.5, 2.5)
        // size = (1 / 2.5, 1 / 2.5) = (0.4, 0.4)
        addWaitStep();
        AddAssert("Scale is 2.5", () => Precision.AlmostEquals(fillContainer.Scale, new Vector2(2.5f)));
        AddAssert("Size is 0.4", () => Precision.AlmostEquals(fillContainer.Size, new Vector2(0.4f)));
    }

    [Test]
    public void TestSeparateStrategy()
    {
        AddStep("Set Separate Strategy", () =>
        {
            fillContainer.Strategy = DrawSizePreservationStrategy.Separate;
            strategyText.Text = "Strategy: Separate (Scale = X:3, Y:2)";
        });

        // scale = (3.0, 2.0)
        // size = (1 / 3.0, 1 / 2.0) ≈ (0.333, 0.5)
        addWaitStep();
        AddAssert("Scale is (3, 2)", () => Precision.AlmostEquals(fillContainer.Scale, new Vector2(3f, 2f)));
        AddAssert("Size is (~0.333, 0.5)", () => Precision.AlmostEquals(fillContainer.Size, new Vector2(1f / 3f, 0.5f)));
    }

    private void addWaitStep()
    {
        AddWaitStep("Wait for layout to compute", 50);
    }
}
