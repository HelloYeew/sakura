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
/// <see cref="ScrollableContainer.ScrollIntoView"/> leaves all of the drawable in view however it is placed inside the
/// content: under padding, from an anchor other than the top-left, and inside a scaled container. It once added up the
/// positions alone, so it stopped short by the padding, and missed altogether a drawable placed from its bottom edge.
/// </summary>
[TestFixture]
public class ScrollIntoViewTest
{
    private const float view_height = 300;
    private const float padding = 16;

    private ManualClock manual = null!;
    private Container root = null!;
    private ScrollableContainer scroll = null!;

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
            Size = new Vector2(400, view_height),
            Clock = new FramedClock(manual)
        };

        root.Add(scroll = new ScrollableContainer { RelativeSizeAxes = Axes.Both });

        root.Load();
        root.CompleteLoad();
    }

    private void settle(int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            manual.CurrentTime += 16;
            root.UpdateSubTree();
        }
    }

    /// <summary>
    /// Scrolls <paramref name="target"/> into view at once and lets the frames that follow lay it out and draw it.
    /// </summary>
    private void scrollIntoView(Drawable target)
    {
        settle(3);
        scroll.ScrollIntoView(target, false);
        settle(3);
    }

    /// <summary>
    /// Asserts that all of <paramref name="target"/> is drawn inside the scroll container's view.
    /// </summary>
    private void assertWhollyInView(Drawable target)
    {
        var view = scroll.ScreenSpaceDrawQuad.BoundingRectangle;
        var drawn = target.ScreenSpaceDrawQuad.BoundingRectangle;

        Assert.Multiple(() =>
        {
            Assert.That(drawn.Y, Is.GreaterThanOrEqualTo(view.Y - 0.5f), "its top is in view");
            Assert.That(drawn.Y + drawn.Height, Is.LessThanOrEqualTo(view.Y + view.Height + 0.5f), "its bottom is in view");
        });
    }

    [Test]
    public void AControlAtTheFootOfAPaddedPageEndsWhollyInView()
    {
        Box target;

        scroll.Add(new Container
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Padding = new MarginPadding(padding),
            Child = new FlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FlowDirection.Vertical,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.X, Height = 1000 },
                    target = new Box { Width = 100, Height = 28 }
                }
            }
        });

        scrollIntoView(target);

        assertWhollyInView(target);
    }

    [Test]
    public void AControlPlacedFromItsBottomEdgeEndsWhollyInView()
    {
        Box target;

        scroll.Add(new Container
        {
            RelativeSizeAxes = Axes.X,
            Height = 2000,
            Child = target = new Box
            {
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                Y = -100,
                Width = 100,
                Height = 28
            }
        });

        scrollIntoView(target);

        assertWhollyInView(target);
    }

    [Test]
    public void AControlInsideAScaledContainerEndsWhollyInView()
    {
        Box target;

        scroll.Add(new Container
        {
            Size = new Vector2(200, 1000),
            Scale = new Vector2(2),
            Child = target = new Box
            {
                Y = 600,
                Width = 100,
                Height = 28
            }
        });

        scrollIntoView(target);

        assertWhollyInView(target);
    }

    [Test]
    public void AControlAboveTheViewEndsWhollyInView()
    {
        Box target;

        scroll.Add(new Container
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Padding = new MarginPadding(padding),
            Child = new FlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FlowDirection.Vertical,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.X, Height = 500 },
                    target = new Box { Width = 100, Height = 28 },
                    new Box { RelativeSizeAxes = Axes.X, Height = 1000 }
                }
            }
        });

        settle(3);
        scroll.ScrollToEnd(false);
        scrollIntoView(target);

        assertWhollyInView(target);
    }
}
