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
/// An auto-sizing container's size is current whenever it's read: before it has updated, through any
/// depth of nesting, for flows too, mid-animation, after its transforms are cleared, and without looping
/// when its layout reads its own size.
/// </summary>
[TestFixture]
public class AutoSizeOnReadTest
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

    private void frame(double step = 16)
    {
        manual.CurrentTime += step;
        root.UpdateSubTree();
    }

    private void load(Drawable drawable)
    {
        root.Add(drawable);
        root.Load();
        root.CompleteLoad();
        frame();
    }

    [Test]
    public void TestReadBeforeAnyUpdate()
    {
        var container = new Container
        {
            AutoSizeAxes = Axes.Both,
            Child = new Box { Size = new Vector2(70, 30) }
        };

        Assert.That(container.Size, Is.EqualTo(new Vector2(70, 30)), "Sized as soon as it's asked, loaded or not.");
    }

    [Test]
    public void TestNewChildCountsAtOnce()
    {
        var container = new Container { AutoSizeAxes = Axes.Both };
        load(container);

        container.Add(new Box { Position = new Vector2(10, 5), Size = new Vector2(40, 40) });

        Assert.That(container.Width, Is.EqualTo(50), "No frame needed between adding and reading.");
    }

    [Test]
    public void TestNestedChainReadsFromTheOutside([Values(2, 5, 10)] int depth)
    {
        var leaf = new Box { Size = new Vector2(50, 20) };
        Drawable current = leaf;

        for (int i = 0; i < depth; i++)
            current = new Container { AutoSizeAxes = Axes.Both, Padding = new MarginPadding(1), Child = current };

        var outer = (Container)current;
        load(outer);

        leaf.Size = new Vector2(120, 45);

        Assert.That(outer.Size, Is.EqualTo(new Vector2(120 + 2 * depth, 45 + 2 * depth)), "Every level resolves inward on the one read.");
    }

    [Test]
    public void TestFlowSizeIsCurrentOnRead()
    {
        var flow = new FlowContainer
        {
            Direction = FlowDirection.Vertical,
            AutoSizeAxes = Axes.Both,
            Spacing = new Vector2(0, 10)
        };
        load(flow);

        flow.Add(new Box { Size = new Vector2(40, 20) });
        flow.Add(new Box { Size = new Vector2(60, 20) });

        Assert.That(flow.Size, Is.EqualTo(new Vector2(60, 50)));
    }

    [Test]
    public void TestHiddenContainerSizesWhenRead()
    {
        var container = new Container { AutoSizeAxes = Axes.Both, Alpha = 0 };
        load(container);

        container.Add(new Box { Size = new Vector2(33, 11) });
        frame();

        Assert.That(container.DrawSize, Is.EqualTo(new Vector2(33, 11)), "Hidden containers skip layout on update, but a read still gets the answer.");
    }

    [Test]
    public void TestAnimatedReadGivesTheSizeReached()
    {
        Box box;
        var container = new Container
        {
            AutoSizeAxes = Axes.X,
            Height = 10,
            AutoSizeDuration = 100,
            Child = box = new Box { Size = new Vector2(100, 10) }
        };
        load(container);
        frame(200);

        box.Width = 300;
        frame(); // the animation starts here
        frame(50);

        Assert.That(container.Width, Is.GreaterThan(100).And.LessThan(300), "Mid-animation, the width the animation has reached.");

        frame(100);

        Assert.That(container.Width, Is.EqualTo(300).Within(0.01f));
    }

    [Test]
    public void TestClearingTransformsDoesNotStickMidAnimation()
    {
        Box box;
        var container = new Container
        {
            AutoSizeAxes = Axes.X,
            Height = 10,
            AutoSizeDuration = 400,
            Child = box = new Box { Size = new Vector2(100, 10) }
        };
        load(container);
        frame(500);

        box.Width = 300;
        frame();
        frame(100);
        Assume.That(container.Width, Is.GreaterThan(100).And.LessThan(300));

        container.ClearTransforms();
        frame(); // a new animation starts from here
        frame(500);

        Assert.That(container.Width, Is.EqualTo(300).Within(0.01f), "It sizes afresh from where it stopped rather than staying there.");
    }

    [Test]
    public void TestFinishingTransformsLandsOnTheTarget()
    {
        Box box;
        var container = new Container
        {
            AutoSizeAxes = Axes.X,
            Height = 10,
            AutoSizeDuration = 400,
            Child = box = new Box { Size = new Vector2(100, 10) }
        };
        load(container);
        frame(500);

        box.Width = 250;
        frame();
        frame(50);
        container.FinishTransforms();

        Assert.That(container.Width, Is.EqualTo(250).Within(0.01f));

        box.Width = 260;
        frame();
        frame(500);

        Assert.That(container.Width, Is.EqualTo(260).Within(0.01f), "Still animates to the next target afterwards.");
    }

    [Test]
    public void TestRelativeChildOfAFlowDoesNotLoop()
    {
        // The flow reads its own size to size this child; that read must not run the layout again.
        var flow = new FlowContainer
        {
            Direction = FlowDirection.Vertical,
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.X, Height = 20 },
                new Box { Size = new Vector2(30, 15) }
            }
        };
        load(flow);

        Assert.Multiple(() =>
        {
            Assert.That(flow.Size.Y, Is.EqualTo(35));
            Assert.That(flow.Children[0].DrawSize.X, Is.EqualTo(800));
        });
    }

    [Test]
    public void TestRelativeChildIsLeftOutOfItsOwnAxis()
    {
        var container = new Container
        {
            AutoSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both },
                new Box { Size = new Vector2(70, 30) }
            }
        };
        load(container);
        frame();

        Assert.That(container.Size, Is.EqualTo(new Vector2(70, 30)), "Can't grow to fit what grows to fit it.");
    }

    [Test]
    public void TestChangingAutoSizeAxesResizes()
    {
        var container = new Container
        {
            Size = new Vector2(10, 10),
            Child = new Box { Size = new Vector2(80, 40) }
        };
        load(container);

        container.AutoSizeAxes = Axes.Y;

        Assert.That(container.Size, Is.EqualTo(new Vector2(10, 40)));
    }

    [Test]
    public void TestPaddingChangeResizes()
    {
        var container = new Container
        {
            AutoSizeAxes = Axes.Both,
            Child = new Box { Size = new Vector2(80, 40) }
        };
        load(container);

        container.Padding = new MarginPadding { Left = 5, Right = 15 };

        Assert.That(container.Size, Is.EqualTo(new Vector2(100, 40)));
    }

    [Test]
    public void TestSettledLayoutDoesNotRerun()
    {
        var container = new CountingContainer
        {
            AutoSizeAxes = Axes.Both,
            Child = new Box { Size = new Vector2(10) }
        };
        load(container);
        frame();

        int before = container.Layouts;

        for (int i = 0; i < 10; i++)
        {
            _ = container.Size;
            frame();
        }

        Assert.That(container.Layouts, Is.EqualTo(before), "Nothing changed, so neither reads nor frames lay it out again.");
    }

    private partial class CountingContainer : Container
    {
        public int Layouts;

        protected override void PerformChildLayout()
        {
            Layouts++;
            base.PerformChildLayout();
        }
    }
}
