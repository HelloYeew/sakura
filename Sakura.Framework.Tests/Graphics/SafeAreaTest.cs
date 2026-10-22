// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Graphics;

[TestFixture]
public class SafeAreaTest
{
    private static readonly MarginPadding insets = new MarginPadding { Top = 40, Left = 60, Bottom = 20, Right = 0 };

    private ManualClock manual = null!;
    private WindowRoot root = null!;
    private SafeAreaDefiningContainer screen = null!;

    [OneTimeSetUp]
    public void InitializeLogger() => Logger.Initialize();

    [OneTimeTearDown]
    public void ShutdownLogger() => Logger.Shutdown();

    [SetUp]
    public void SetUp()
    {
        manual = new ManualClock { CurrentTime = 1000 };
        root = new WindowRoot
        {
            Size = new Vector2(800, 600),
            Clock = new FramedClock(manual)
        };

        root.Add(screen = new SafeAreaDefiningContainer { OverrideSafeArea = insets });

        root.Load();
        root.CompleteLoad();
    }

    private void settle()
    {
        for (int i = 0; i < 3; i++)
        {
            manual.CurrentTime += 16;
            root.UpdateSubTree();
        }
    }

    private static void assertRect(RectangleF actual, float x, float y, float width, float height, string? message = null)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(actual.X, Is.EqualTo(x).Within(0.01f), message);
            Assert.That(actual.Y, Is.EqualTo(y).Within(0.01f), message);
            Assert.That(actual.Width, Is.EqualTo(width).Within(0.01f), message);
            Assert.That(actual.Height, Is.EqualTo(height).Within(0.01f), message);
        }
    }

    private static void assertPadding(MarginPadding actual, float top, float left, float bottom, float right, string? message = null)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(actual.Top, Is.EqualTo(top).Within(0.01f), message);
            Assert.That(actual.Left, Is.EqualTo(left).Within(0.01f), message);
            Assert.That(actual.Bottom, Is.EqualTo(bottom).Within(0.01f), message);
            Assert.That(actual.Right, Is.EqualTo(right).Within(0.01f), message);
        }
    }

    [Test]
    public void TestContentIsSafe()
    {
        var content = new Box { RelativeSizeAxes = Axes.Both };
        screen.Add(content);
        settle();

        assertPadding(screen.Padding, 40, 60, 20, 0);
        assertPadding(screen.SafeAreaPadding.Value, 40, 60, 20, 0);
        assertRect(content.DrawRectangle, 60, 40, 740, 540);
    }

    [Test]
    public void TestBleedReachesTheScreenEdge()
    {
        var background = new Box { RelativeSizeAxes = Axes.Both, Bleed = Edges.All };
        screen.Add(background);
        settle();

        assertRect(background.DrawRectangle, 0, 0, 800, 600);
        Assert.That(background.DrawSize.X, Is.EqualTo(800).Within(0.01f), "DrawSize includes the extension, so relative children fill it.");
        Assert.That(background.DrawSize.Y, Is.EqualTo(600).Within(0.01f));
    }

    [Test]
    public void TestBleedOnlyOnChosenEdges()
    {
        var background = new Box { RelativeSizeAxes = Axes.Both, Bleed = Edges.Top };
        screen.Add(background);
        settle();

        assertRect(background.DrawRectangle, 60, 0, 740, 580);
    }

    [Test]
    public void TestEdgeInsideTheSafeAreaStays()
    {
        var box = new Box { Position = new Vector2(200, 200), Size = new Vector2(100), Bleed = Edges.All };
        screen.Add(box);
        settle();

        assertRect(box.DrawRectangle, 260, 240, 100, 100);
        Assert.That(box.DrawSize.X, Is.EqualTo(100).Within(0.01f));
    }

    [Test]
    public void TestEdgeBeyondTheBoundaryBleeds()
    {
        // Half out of the safe area at the top, as a bar sliding in from above would be.
        var box = new Box { Position = new Vector2(300, -30), Size = new Vector2(100, 50), Bleed = Edges.All };
        screen.Add(box);
        settle();

        assertRect(box.DrawRectangle, 360, 0, 100, 60);
    }

    [Test]
    public void TestBarBackgroundBleedsButBarStays()
    {
        Box barBackground;
        Container bar;

        screen.Add(bar = new Container
        {
            Anchor = Anchor.BottomLeft,
            Origin = Anchor.BottomLeft,
            RelativeSizeAxes = Axes.X,
            Height = 50,
            Child = barBackground = new Box { RelativeSizeAxes = Axes.Both, Bleed = Edges.All }
        });
        settle();

        assertRect(bar.DrawRectangle, 60, 530, 740, 50, "The bar itself stays in the safe area.");
        assertRect(barBackground.DrawRectangle, 0, 530, 800, 70, "Its background reaches the left and bottom edges; the top is inside the safe area.");
    }

    [TestCase(Anchor.TopLeft)]
    [TestCase(Anchor.Centre)]
    [TestCase(Anchor.BottomRight)]
    public void TestBleedIgnoresAnchorAndOrigin(Anchor anchor)
    {
        var background = new Box { Anchor = anchor, Origin = anchor, RelativeSizeAxes = Axes.Both, Bleed = Edges.All };
        screen.Add(background);
        settle();

        assertRect(background.DrawRectangle, 0, 0, 800, 600);
    }

    [Test]
    public void TestBleedUnderAScaledContainer()
    {
        // 740x540 of safe area over a 370x270 target is a scale of exactly 2.
        var background = new Box { RelativeSizeAxes = Axes.Both, Bleed = Edges.All };
        screen.Add(new DrawSizePreservingFillContainer
        {
            TargetDrawSize = new Vector2(370, 270),
            Child = background
        });
        settle();

        assertRect(background.DrawRectangle, 0, 0, 800, 600);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(background.AppliedDrawSize.X, Is.EqualTo(400).Within(0.01f), "The extension is in the scaled container's units.");
            Assert.That(background.AppliedDrawSize.Y, Is.EqualTo(300).Within(0.01f));
        }
    }

    [Test]
    public void TestRelativeChildrenFillTheBleed()
    {
        var child = new Box { RelativeSizeAxes = Axes.Both };
        screen.Add(new Container
        {
            RelativeSizeAxes = Axes.Both,
            Bleed = Edges.All,
            Child = child
        });
        settle();

        assertRect(child.DrawRectangle, 0, 0, 800, 600);
    }

    [Test]
    public void TestSafeAreaContainerInSafeContentPadsNothing()
    {
        var safe = new SafeAreaContainer();
        screen.Add(safe);
        settle();

        assertPadding(safe.Padding, 0, 0, 0, 0);
    }

    [Test]
    public void TestSafeAreaContainerInsideBleed()
    {
        var content = new Box { RelativeSizeAxes = Axes.Both };
        SafeAreaContainer safe;

        screen.Add(new Container
        {
            RelativeSizeAxes = Axes.Both,
            Bleed = Edges.All,
            Child = safe = new SafeAreaContainer { Child = content }
        });
        settle();

        assertPadding(safe.Padding, 40, 60, 20, 0);
        assertRect(content.DrawRectangle, 60, 40, 740, 540);
    }

    [Test]
    public void TestSafeAreaContainerPadsInItsOwnUnits()
    {
        var content = new Box { RelativeSizeAxes = Axes.Both };
        SafeAreaContainer safe;

        // Half-size and scaled by two, so it covers the whole screen in units half the size of the screen's.
        screen.Add(new Container
        {
            RelativeSizeAxes = Axes.Both,
            Bleed = Edges.All,
            Child = new Container
            {
                Size = new Vector2(400, 300),
                Scale = new Vector2(2),
                Child = safe = new SafeAreaContainer { Child = content }
            }
        });
        settle();

        assertPadding(safe.Padding, 20, 30, 10, 0);
        assertRect(content.DrawRectangle, 60, 40, 740, 540);
    }

    [Test]
    public void TestSafeAreaContainerOverrideEdges()
    {
        SafeAreaContainer safe;

        screen.Add(new Container
        {
            RelativeSizeAxes = Axes.Both,
            Bleed = Edges.All,
            Child = safe = new SafeAreaContainer { SafeAreaOverrideEdges = Edges.Top }
        });
        settle();

        assertPadding(safe.Padding, 0, 60, 20, 0);
    }

    [Test]
    public void TestChangingInsetsLaysEverythingOutAgain()
    {
        var content = new Box { RelativeSizeAxes = Axes.Both };
        var background = new Box { RelativeSizeAxes = Axes.Both, Bleed = Edges.All };
        SafeAreaContainer safe;

        screen.Add(background);
        screen.Add(new Container
        {
            RelativeSizeAxes = Axes.Both,
            Bleed = Edges.All,
            Child = safe = new SafeAreaContainer { Child = content }
        });
        settle();

        screen.OverrideSafeArea = new MarginPadding { Left = 100 };
        settle();

        assertRect(background.DrawRectangle, 0, 0, 800, 600);
        assertPadding(safe.Padding, 0, 100, 0, 0);
        assertRect(content.DrawRectangle, 100, 0, 700, 600);

        screen.OverrideSafeArea = new MarginPadding();
        settle();

        assertRect(background.DrawRectangle, 0, 0, 800, 600);
        Assert.That(background.DrawSize.X, Is.EqualTo(800).Within(0.01f), "Nothing to bleed into, so nothing added.");
        assertPadding(safe.Padding, 0, 0, 0, 0);
    }

    [Test]
    public void TestRemovingBleedRestoresTheSafeRectangle()
    {
        var background = new Box { RelativeSizeAxes = Axes.Both, Bleed = Edges.All };
        screen.Add(background);
        settle();

        background.Bleed = Edges.None;
        settle();

        assertRect(background.DrawRectangle, 60, 40, 740, 540);
        Assert.That(background.DrawSize.X, Is.EqualTo(740).Within(0.01f));
    }

    [Test]
    public void TestNestedDefinitionPassesTheOuterOneThrough()
    {
        var background = new Box { RelativeSizeAxes = Axes.Both, Bleed = Edges.All };
        var content = new Box { RelativeSizeAxes = Axes.Both };
        SafeAreaDefiningContainer inner;

        screen.Add(new Container
        {
            RelativeSizeAxes = Axes.Both,
            Bleed = Edges.All,
            Child = inner = new SafeAreaDefiningContainer
            {
                Children = new Drawable[] { background, content }
            }
        });
        settle();

        assertPadding(inner.Padding, 40, 60, 20, 0);
        assertRect(content.DrawRectangle, 60, 40, 740, 540);
        assertRect(background.DrawRectangle, 0, 0, 800, 600);
    }

    [Test]
    public void TestNestedOverrideIsAScreenOfItsOwn()
    {
        var background = new Box { RelativeSizeAxes = Axes.Both, Bleed = Edges.All };
        var content = new Box { RelativeSizeAxes = Axes.Both };

        // A 200x100 simulated screen in the middle of the real one, with a 10 unit notch on its left.
        screen.Add(new SafeAreaDefiningContainer
        {
            RelativeSizeAxes = Axes.None,
            Position = new Vector2(100, 100),
            Size = new Vector2(200, 100),
            OverrideSafeArea = new MarginPadding { Left = 10 },
            Children = new Drawable[] { background, content }
        });
        settle();

        assertRect(content.DrawRectangle, 170, 140, 190, 100);
        assertRect(background.DrawRectangle, 160, 140, 200, 100, "Bleeding stops at the simulated screen's edge.");
    }

    [Test]
    public void TestRotatedDrawableDoesNotBleed()
    {
        var box = new Box { RelativeSizeAxes = Axes.Both, Bleed = Edges.All, Rotation = 90 };
        screen.Add(box);
        settle();

        Assert.That(box.AppliedDrawSize.X, Is.EqualTo(740).Within(0.01f));
        Assert.That(box.AppliedDrawSize.Y, Is.EqualTo(540).Within(0.01f));
    }

    [Test]
    public void TestFollowsTheWindowWhenNotOverridden()
    {
        var content = new Box { RelativeSizeAxes = Axes.Both };
        var background = new Box { RelativeSizeAxes = Axes.Both, Bleed = Edges.All };

        screen.OverrideSafeArea = null;
        screen.Add(background);
        screen.Add(content);
        settle();

        assertRect(content.DrawRectangle, 0, 0, 800, 600, "The headless window has no insets.");

        // Changes no geometry: the definition has to notice the window and lay everything out again itself.
        root.Window.SafeAreaPadding.Value = new MarginPadding { Top = 30, Bottom = 10 };
        settle();

        assertPadding(screen.Padding, 30, 0, 10, 0);
        assertRect(content.DrawRectangle, 0, 30, 800, 560);
        assertRect(background.DrawRectangle, 0, 0, 800, 600);
    }

    [Test]
    public void TestNestedBleedFollowsTheWindow()
    {
        Box headerBackground;
        screen.OverrideSafeArea = null;
        screen.Add(new Container
        {
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1),
            Child = new Container
            {
                RelativeSizeAxes = Axes.X,
                Size = new Vector2(1, 40),
                Child = headerBackground = new Box { RelativeSizeAxes = Axes.Both, Size = new Vector2(1), Bleed = Edges.All }
            }
        });
        settle();

        root.Window.SafeAreaPadding.Value = new MarginPadding { Top = 30, Bottom = 10 };
        settle();

        assertRect(headerBackground.DrawRectangle, 0, 0, 800, 70);
    }

    [Test]
    public void TestPreviewFollowsOrientationAndScalesWithTheWindow()
    {
        // Twice an iPhone's short side, so twice its insets.
        var landscape = SafeAreaPreviewOverlay.InsetsFor(SafeAreaPreview.Iphone, new Vector2(1600, 786));
        Assert.That(landscape, Is.Not.Null);
        assertPadding(landscape!.Value, 0, 118, 42, 118);

        var portrait = SafeAreaPreviewOverlay.InsetsFor(SafeAreaPreview.Iphone, new Vector2(393, 852));
        Assert.That(portrait, Is.Not.Null);
        assertPadding(portrait!.Value, 59, 0, 34, 0);

        Assert.That(SafeAreaPreviewOverlay.InsetsFor(SafeAreaPreview.Off, new Vector2(800, 600)), Is.Null);
    }

    /// <summary>
    /// A root that caches a window after its children have loaded, which is the order <see cref="App"/>
    /// does it in.
    /// </summary>
    private partial class WindowRoot : Container
    {
        public readonly HeadlessWindow Window = new HeadlessWindow();

        public override void Load()
        {
            base.Load();

            Cache<IWindow>(Window);
        }
    }
}
