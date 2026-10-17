// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Containers;

/// <summary>
/// Auto-sizing panels (dark) around one child (coloured) placed by different anchors: the panel fits the
/// child where its anchor puts it, so a centred child sits in the middle of a snug panel and a
/// right-held one keeps its gap to the right edge. Also bypass, and a minimum/maximum.
/// </summary>
public partial class TestAnchoredAutoSize : TestScene
{
    private Container topLeftPanel = null!;
    private Container centrePanel = null!;
    private Container rightPanel = null!;
    private Container limitedPanel = null!;

    private Box topLeftChild = null!;
    private Box centreChild = null!;
    private Box rightChild = null!;
    private Box limitedChild = null!;
    private Box bypassChild = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("Build panels", () =>
        {
            Clear();

            Add(new FlowContainer
            {
                Position = new Vector2(40),
                AutoSizeAxes = Axes.Both,
                Direction = FlowDirection.Vertical,
                Spacing = new Vector2(0, 30),
                Children = new Drawable[]
                {
                    labelled("TopLeft child at (10, 10)", topLeftPanel = panel(topLeftChild = child(Anchor.TopLeft, new Vector2(10), Color.SteelBlue))),
                    labelled("Centre child, 20px right of centre", centrePanel = panel(centreChild = child(Anchor.Centre, new Vector2(20, 0), Color.SeaGreen))),
                    labelled("CentreRight child held 16px in", rightPanel = panel(rightChild = child(Anchor.CentreRight, new Vector2(-16, 0), Color.DarkOrange))),
                    labelled("Minimum 200 wide, maximum 60 tall, eased; the long bar bypasses X", limitedPanel = panel(
                        limitedChild = child(Anchor.TopLeft, Vector2.Zero, Color.MediumPurple),
                        bypassChild = new Box { Size = new Vector2(400, 6), Y = 50, Color = Color.Crimson, BypassAutoSizeAxes = Axes.X }))
                }
            });

            limitedPanel.AutoSizeMinimum = new Vector2(200, 0);
            limitedPanel.AutoSizeMaximum = new Vector2(float.PositiveInfinity, 60);
            limitedPanel.AutoSizeDuration = 300;
            limitedPanel.AutoSizeEasing = Easing.OutQuint;
        });
    }

    private static Container panel(params Drawable[] children)
    {
        var container = new Container
        {
            AutoSizeAxes = Axes.Both,
            Child = new Box { RelativeSizeAxes = Axes.Both, Color = Color.DarkSlateGray }
        };

        container.AddRange(children);
        return container;
    }

    private static Box child(Anchor anchor, Vector2 position, Color color) => new Box
    {
        Anchor = anchor,
        Origin = anchor,
        Position = position,
        Size = new Vector2(80, 40),
        Color = color
    };

    private static Drawable labelled(string text, Drawable content) => new FlowContainer
    {
        AutoSizeAxes = Axes.Both,
        Direction = FlowDirection.Vertical,
        Spacing = new Vector2(0, 6),
        Children = new Drawable[]
        {
            new SpriteText { Text = text, Color = Color.White },
            content
        }
    };

    [Test]
    public void TestPanelsFitTheirChildren()
    {
        AddAssert("TopLeft: position plus size", () => topLeftPanel.Size == new Vector2(90, 50));
        AddAssert("Centre: grows evenly both ways", () => centrePanel.Size == new Vector2(120, 40));
        AddAssert("CentreRight: size plus the gap", () => rightPanel.Size == new Vector2(96, 40));
        AddUntilStep("Limited: eases in, held at the minimum width", () => Precision(limitedPanel.Size, new Vector2(200, 56)));
    }

    [Test]
    public void TestGrowingChildren()
    {
        AddStep("Grow every child", () =>
        {
            foreach (var box in new[] { topLeftChild, centreChild, rightChild })
                box.Size = new Vector2(160, 70);
        });
        AddAssert("Read at once, before a frame", () => centrePanel.Size == new Vector2(200, 70) && rightPanel.Size == new Vector2(176, 70));

        AddStep("Move the centre child further right", () => centreChild.X = 50);
        AddAssert("Grows on both sides", () => centrePanel.Width == 260);

        AddStep("Make the limited child big", () => limitedChild.Size = new Vector2(320, 200));
        AddUntilStep("Eases out past the minimum, capped in height", () => Precision(limitedPanel.Size, new Vector2(320, 60)));
    }

    [Test]
    public void TestBypass()
    {
        AddUntilStep("The long bar doesn't widen it", () => Precision(limitedPanel.Size, new Vector2(200, 56)));
        AddStep("Stop bypassing", () => bypassChild.BypassAutoSizeAxes = Axes.None);
        AddUntilStep("Now it does", () => Precision(limitedPanel.Size, new Vector2(400, 56)));
    }

    private static bool Precision(Vector2 a, Vector2 b) => System.Math.Abs(a.X - b.X) < 0.5f && System.Math.Abs(a.Y - b.Y) < 0.5f;
}
