// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// Auto-size counts each child where its anchor, origin, position, scale, and margins put it (osu!'s
/// <c>RequiredParentSizeToFit</c>): a centred child grows the container evenly, an end-anchored one from
/// the end. Bypassed axes, relative positions, and the minimum/maximum are honored too.
/// </summary>
[TestFixture]
public class AnchoredAutoSizeTest
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

    private Container fit(params Drawable[] children)
    {
        var container = new Container { AutoSizeAxes = Axes.Both, Children = children };
        root.Add(container);
        root.Load();
        root.CompleteLoad();
        frame();
        frame();
        return container;
    }

    private static Box box(Anchor anchor, Anchor origin, Vector2 position, Vector2 size) => new Box
    {
        Anchor = anchor,
        Origin = origin,
        Position = position,
        Size = size
    };

    [Test]
    public void TestTopLeftIsPositionPlusSize()
    {
        var container = fit(box(Anchor.TopLeft, Anchor.TopLeft, new Vector2(10, 20), new Vector2(50, 30)));
        Assert.That(container.Size, Is.EqualTo(new Vector2(60, 50)));
    }

    [Test]
    public void TestCentredChildFitsExactly()
    {
        var container = fit(box(Anchor.Centre, Anchor.Centre, Vector2.Zero, new Vector2(50, 30)));
        Assert.That(container.Size, Is.EqualTo(new Vector2(50, 30)));
    }

    [Test]
    public void TestOffsetCentredChildGrowsBothWays()
    {
        // 10px right of centre: room for 25 + 10 to the right, so 35 to the left as well.
        var container = fit(box(Anchor.Centre, Anchor.Centre, new Vector2(10, 0), new Vector2(50, 30)));
        Assert.That(container.Size, Is.EqualTo(new Vector2(70, 30)));
    }

    [Test]
    public void TestEndAnchoredChildCountsFromTheEnd()
    {
        // A label held 16px in from the right, as the footer's screen name is.
        var container = fit(box(Anchor.CentreRight, Anchor.CentreRight, new Vector2(-16, 0), new Vector2(120, 30)));
        Assert.That(container.Size, Is.EqualTo(new Vector2(136, 30)));
    }

    [Test]
    public void TestOriginOffsetsAStartAnchor()
    {
        // Centre origin at (50, 50): half the box is before the position.
        var container = fit(box(Anchor.TopLeft, Anchor.Centre, new Vector2(50, 50), new Vector2(40, 20)));
        Assert.That(container.Size, Is.EqualTo(new Vector2(70, 60)));
    }

    [Test]
    public void TestMarginsWidenACentredChild()
    {
        var child = box(Anchor.Centre, Anchor.Centre, Vector2.Zero, new Vector2(50, 30));
        child.Margin = new MarginPadding { Left = 10, Right = 4, Top = 2, Bottom = 6 };

        var container = fit(child);

        Assert.That(container.Size, Is.EqualTo(new Vector2(64, 38)));
    }

    [Test]
    public void TestScaleCountsWithTheOrigin()
    {
        var child = box(Anchor.Centre, Anchor.Centre, Vector2.Zero, new Vector2(50, 30));
        child.Scale = new Vector2(2);

        var container = fit(child);

        Assert.That(container.Size, Is.EqualTo(new Vector2(100, 60)));
    }

    [Test]
    public void TestMixedChildrenTakeTheLargestNeed()
    {
        var container = fit(
            box(Anchor.TopLeft, Anchor.TopLeft, Vector2.Zero, new Vector2(30, 10)),
            box(Anchor.TopCentre, Anchor.TopCentre, Vector2.Zero, new Vector2(80, 10)),
            box(Anchor.BottomRight, Anchor.BottomRight, Vector2.Zero, new Vector2(10, 50)));

        Assert.That(container.Size, Is.EqualTo(new Vector2(80, 50)));
    }

    [Test]
    public void TestChildrenLandInsideTheSizeTheyAskedFor()
    {
        Box centred, right;
        var container = fit(
            centred = box(Anchor.Centre, Anchor.Centre, new Vector2(10, 0), new Vector2(50, 30)),
            right = box(Anchor.CentreRight, Anchor.CentreRight, new Vector2(-16, 0), new Vector2(20, 20)));
        container.Position = new Vector2(100, 100);
        frame();

        RectangleF bounds = container.DrawRectangle;

        foreach (var child in new Drawable[] { centred, right })
        {
            RectangleF rect = child.DrawRectangle;

            Assert.That(rect.X, Is.GreaterThanOrEqualTo(bounds.X - 0.01f));
            Assert.That(rect.X + rect.Width, Is.LessThanOrEqualTo(bounds.X + bounds.Width + 0.01f));
        }

        Assert.That(right.DrawRectangle.X + right.DrawRectangle.Width, Is.EqualTo(bounds.X + bounds.Width - 16).Within(0.01f));
    }

    [Test]
    public void TestBypassedAxisIsLeftOut()
    {
        var wide = box(Anchor.TopLeft, Anchor.TopLeft, Vector2.Zero, new Vector2(500, 10));
        wide.BypassAutoSizeAxes = Axes.X;

        var container = fit(wide, box(Anchor.TopLeft, Anchor.TopLeft, Vector2.Zero, new Vector2(40, 40)));

        Assert.That(container.Size, Is.EqualTo(new Vector2(40, 40)), "Bypassing X: its width is left out, its height still counts.");

        wide.BypassAutoSizeAxes = Axes.None;

        Assert.That(container.Size, Is.EqualTo(new Vector2(500, 40)), "Turning bypass off resizes at once.");
    }

    [Test]
    public void TestRelativePositionDoesNotSizeTheParent()
    {
        var child = box(Anchor.TopLeft, Anchor.TopLeft, new Vector2(0.5f, 7), new Vector2(40, 10));
        child.RelativePositionAxes = Axes.X;

        var container = fit(child);

        Assert.That(container.Size, Is.EqualTo(new Vector2(40, 17)));
    }

    [Test]
    public void TestMinimumAndMaximumIncludePadding()
    {
        var container = new Container
        {
            AutoSizeAxes = Axes.Both,
            Padding = new MarginPadding(5),
            AutoSizeMinimum = new Vector2(100, 0),
            AutoSizeMaximum = new Vector2(float.PositiveInfinity, 30),
            Child = new Box { Size = new Vector2(20, 60) }
        };
        root.Add(container);
        root.Load();
        root.CompleteLoad();
        frame();

        Assert.That(container.Size, Is.EqualTo(new Vector2(100, 30)), "Short text keeps the minimum; tall content is capped.");

        ((Box)container.Children[0]).Width = 200;

        Assert.That(container.Size.X, Is.EqualTo(210), "Past the minimum it fits the content again.");
    }

    [Test]
    public void TestMinimumAboveMaximumWins()
    {
        var container = new Container
        {
            AutoSizeAxes = Axes.X,
            Height = 10,
            AutoSizeMinimum = new Vector2(80, 0),
            AutoSizeMaximum = new Vector2(50, 0),
            Child = new Box { Size = new Vector2(20, 10) }
        };

        Assert.That(container.Size.X, Is.EqualTo(80), "A minimum is a promise; it isn't broken by a smaller maximum.");
    }

    [Test]
    public void TestRequiredExtentMatchesTheFormula()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Container.RequiredExtent(10, 50, 0, 0, 0), Is.EqualTo(60));
            Assert.That(Container.RequiredExtent(0, 50, 0, 0.5f, 0.5f), Is.EqualTo(50));
            Assert.That(Container.RequiredExtent(10, 50, 0, 0.5f, 0.5f), Is.EqualTo(70));
            Assert.That(Container.RequiredExtent(-16, 120, 0, 1, 1), Is.EqualTo(136));
            Assert.That(Container.RequiredExtent(50, 40, 0, 0, 0.5f), Is.EqualTo(70));
            Assert.That(Container.RequiredExtent(0, 50, 14, 0.5f, 0.5f), Is.EqualTo(64));
        });
    }
}
