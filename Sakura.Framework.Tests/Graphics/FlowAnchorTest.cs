// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// Flows keep their children's anchors and origins: along the flow a child takes its slot whatever its
/// anchor; across it the anchor aligns it within its line (or the whole flow, for one line that isn't
/// auto-sized across); and scale pivots on the child's own origin.
/// </summary>
[TestFixture]
public class FlowAnchorTest
{
    private ManualClock manual = null!;
    private Container root = null!;

    [OneTimeSetUp]
    public void InitializeLogger() => Logger.Initialize();

    [OneTimeTearDown]
    public void ShutdownLogger() => Logger.Shutdown();

    [SetUp]
    public void SetUp()
    {
        manual = new ManualClock { CurrentTime = 1000 };
        root = new Container
        {
            Size = new Vector2(800, 600),
            Clock = new FramedClock(manual)
        };
    }

    private void frame()
    {
        manual.CurrentTime += 16;
        root.UpdateSubTree();
    }

    private void load(Drawable drawable)
    {
        root.Add(drawable);
        root.Load();
        root.CompleteLoad();
        frame();
        frame();
    }

    /// <summary>Where a child is drawn, relative to its flow's top-left.</summary>
    private static Vector2 placed(Drawable child, Drawable flow) =>
        new Vector2(child.DrawRectangle.X - flow.DrawRectangle.X, child.DrawRectangle.Y - flow.DrawRectangle.Y);

    private static void assertAt(Drawable child, Drawable flow, Vector2 expected, string? message = null)
    {
        Vector2 actual = placed(child, flow);
        Assert.That(actual.X, Is.EqualTo(expected.X).Within(0.01f), message);
        Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(0.01f), message);
    }

    private static Vector2 centreOf(RectangleF rect) => new Vector2(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);

    private static Box box(float width, float height, Anchor anchor = Anchor.TopLeft) => new Box
    {
        Size = new Vector2(width, height),
        Anchor = anchor,
        Origin = anchor
    };

    [Test]
    public void TestAnchorsAreKept()
    {
        Box child;
        var flow = new FlowContainer
        {
            Direction = FlowDirection.Vertical,
            AutoSizeAxes = Axes.Both,
            Child = child = box(40, 20, Anchor.TopCentre)
        };
        load(flow);

        Assert.Multiple(() =>
        {
            Assert.That(child.Anchor, Is.EqualTo(Anchor.TopCentre));
            Assert.That(child.Origin, Is.EqualTo(Anchor.TopCentre));
        });
    }

    [Test]
    public void TestVerticalStackCentresAndRightAligns()
    {
        Box wide, centred, right;
        var flow = new FlowContainer
        {
            Direction = FlowDirection.Vertical,
            AutoSizeAxes = Axes.Both,
            Spacing = new Vector2(0, 5),
            Children = new Drawable[]
            {
                wide = box(100, 20),
                centred = box(40, 20, Anchor.TopCentre),
                right = box(30, 20, Anchor.TopRight)
            }
        };
        load(flow);

        Assert.Multiple(() =>
        {
            Assert.That(flow.Size, Is.EqualTo(new Vector2(100, 70)));
            assertAt(wide, flow, new Vector2(0, 0));
            assertAt(centred, flow, new Vector2(30, 25), "Centred under the widest.");
            assertAt(right, flow, new Vector2(70, 50), "Flush right.");
        });
    }

    [Test]
    public void TestSingleLineAlignsWithinTheWholeFlow()
    {
        Box child;
        var flow = new FlowContainer
        {
            Direction = FlowDirection.Horizontal,
            Size = new Vector2(300, 60),
            Children = new Drawable[]
            {
                box(30, 20),
                child = box(30, 20, Anchor.CentreLeft)
            }
        };
        load(flow);

        assertAt(child, flow, new Vector2(30, 20), "Centred in the flow's 60px height, not the line's 20.");
    }

    [Test]
    public void TestWrappedLinesAlignWithinTheirLine()
    {
        Box tall, small, nextLine;
        var flow = new FlowContainer
        {
            Direction = FlowDirection.Horizontal,
            Size = new Vector2(100, 200),
            Children = new Drawable[]
            {
                tall = box(50, 40),
                small = box(50, 10, Anchor.BottomLeft),
                nextLine = box(50, 10, Anchor.CentreLeft)
            }
        };
        load(flow);

        Assert.Multiple(() =>
        {
            assertAt(tall, flow, new Vector2(0, 0));
            assertAt(small, flow, new Vector2(50, 30), "Bottom of its 40px line.");
            assertAt(nextLine, flow, new Vector2(0, 40), "Alone on its 10px line, there's nothing to centre in.");
        });
    }

    [Test]
    public void TestFlowAxisIgnoresTheAnchor([Values(Anchor.TopLeft, Anchor.TopCentre, Anchor.TopRight)] Anchor anchor)
    {
        Box second;
        var flow = new FlowContainer
        {
            Direction = FlowDirection.Horizontal,
            Size = new Vector2(400, 20),
            Spacing = new Vector2(10, 0),
            Children = new Drawable[]
            {
                box(30, 20, anchor),
                second = box(30, 20, anchor)
            }
        };
        load(flow);

        Assert.That(placed(second, flow).X, Is.EqualTo(40).Within(0.01f), "A slot along the flow is a slot, whatever the anchor says.");
    }

    [Test]
    public void TestAlignmentStillPlacesTheLine()
    {
        Box child;
        var flow = new FlowContainer
        {
            Direction = FlowDirection.Horizontal,
            Size = new Vector2(200, 50),
            Alignment = FlowAlignment.End,
            Child = child = box(40, 20, Anchor.BottomCentre)
        };
        load(flow);

        assertAt(child, flow, new Vector2(160, 30));
    }

    [Test]
    public void TestScalePivotsOnTheOrigin()
    {
        Box child;
        var flow = new FlowContainer
        {
            Direction = FlowDirection.Vertical,
            AutoSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                box(100, 40),
                child = new Box { Size = new Vector2(40, 20), Anchor = Anchor.TopCentre, Origin = Anchor.Centre }
            }
        };
        load(flow);

        Vector2 centre = centreOf(child.DrawRectangle);

        child.Scale = new Vector2(2);
        frame();

        Vector2 after = centreOf(child.DrawRectangle);
        Assert.That(after.X, Is.EqualTo(centre.X).Within(0.01f), "Grows about its own centre, not its corner.");
        Assert.That(after.Y, Is.EqualTo(centre.Y).Within(0.01f));
    }

    [Test]
    public void TestMarginsAndPaddingWithAnchors()
    {
        Box child;
        var flow = new FlowContainer
        {
            Direction = FlowDirection.Vertical,
            Size = new Vector2(200, 100),
            Padding = new MarginPadding { Left = 10, Right = 30, Top = 5 },
            Child = child = new Box
            {
                Size = new Vector2(40, 20),
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                Margin = new MarginPadding { Right = 8, Top = 4 }
            }
        };
        load(flow);

        // Content runs 10..170; the margin box ends at 170, the box 8 before it.
        assertAt(child, flow, new Vector2(122, 9));
    }

    [Test]
    public void TestRelativeChildWithMarginsFillsItsSlot()
    {
        Box child, after;
        var flow = new FlowContainer
        {
            Direction = FlowDirection.Horizontal,
            Size = new Vector2(200, 20),
            Children = new Drawable[]
            {
                child = new Box { RelativeSizeAxes = Axes.X, Width = 0.5f, Height = 20, Margin = new MarginPadding { Left = 10, Right = 10 } },
                after = box(20, 20)
            }
        };
        load(flow);

        Assert.Multiple(() =>
        {
            Assert.That(child.DrawSize.X, Is.EqualTo(80));
            Assert.That(placed(child, flow).X, Is.EqualTo(10).Within(0.01f));
            Assert.That(placed(after, flow).X, Is.EqualTo(100).Within(0.01f), "The margin box is half the flow; the next slot starts after it.");
        });
    }

    [Test]
    public void TestBypassedChildTakesASlotButNotTheSize()
    {
        Box wide;
        var flow = new FlowContainer
        {
            Direction = FlowDirection.Vertical,
            AutoSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                box(50, 20),
                wide = box(300, 20)
            }
        };
        wide.BypassAutoSizeAxes = Axes.X;
        load(flow);

        Assert.Multiple(() =>
        {
            Assert.That(flow.Size, Is.EqualTo(new Vector2(50, 40)));
            assertAt(wide, flow, new Vector2(0, 20));
        });
    }

    [Test]
    public void TestAutoSizeLimitsApplyToFlows()
    {
        var flow = new FlowContainer
        {
            Direction = FlowDirection.Horizontal,
            AutoSizeAxes = Axes.Both,
            AutoSizeMinimum = new Vector2(120, 0),
            Child = box(20, 20)
        };
        load(flow);

        Assert.That(flow.Size, Is.EqualTo(new Vector2(120, 20)));
    }
}
