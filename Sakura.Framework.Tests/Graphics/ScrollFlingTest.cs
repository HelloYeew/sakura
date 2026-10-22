// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// How far a drag on a scroll container throws the content once it's let go.
/// </summary>
[TestFixture]
public class ScrollFlingTest
{
    private const float start = 5000;

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
            Size = new Vector2(800, 600),
            Clock = new FramedClock(manual)
        };

        root.Add(scroll = new ScrollableContainer { RelativeSizeAxes = Axes.Both });
        scroll.Add(new Box { RelativeSizeAxes = Axes.X, Height = 20000 });

        root.Load();
        root.CompleteLoad();
        settle(3);

        scroll.CurrentScroll = new Vector2(0, start);
    }

    private void settle(int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            manual.CurrentTime += 16;
            root.UpdateSubTree();
        }
    }

    private static MouseButtonEvent press(double timestamp = double.NaN) => new MouseButtonEvent(new MouseState(), MouseButton.Left, 1, timestamp);

    private static MouseEvent move(float dy, double timestamp = double.NaN) => new MouseEvent(new MouseState(), new Vector2(0, dy), timestamp);

    /// <summary>
    /// Where the content comes to rest, once the throw has run out.
    /// </summary>
    private float restingScroll()
    {
        settle(300);
        return scroll.CurrentScroll.Y;
    }

    /// <summary>
    /// A phone sends touch moves faster than frames: they come several to an update, and the clock reads no time
    /// between them. The time they happened at is what tells how fast the finger went.
    /// </summary>
    [Test]
    public void MovesInOneFrameThrowByWhenTheyHappened()
    {
        scroll.OnDragStart(press(0));

        for (int i = 1; i <= 5; i++)
            scroll.OnDrag(move(-20, i * 4));

        scroll.OnDragEnd(press(24));

        Assert.That(restingScroll(), Is.GreaterThan(start + 100 + 50), "the 100 dragged, and thrown on from there");
    }

    /// <summary>
    /// A finger that stops before lifting puts the content down where it is.
    /// </summary>
    [Test]
    public void APauseBeforeLettingGoThrowsNothing()
    {
        scroll.OnDragStart(press());

        for (int i = 0; i < 5; i++)
        {
            manual.CurrentTime += 16;
            root.UpdateSubTree();
            scroll.OnDrag(move(-20));
        }

        settle(30);
        scroll.OnDragEnd(press());

        Assert.That(restingScroll(), Is.EqualTo(start + 100).Within(1));
    }

    [Test]
    public void ADragLetGoAtSpeedIsThrown()
    {
        scroll.OnDragStart(press());

        for (int i = 0; i < 5; i++)
        {
            manual.CurrentTime += 16;
            root.UpdateSubTree();
            scroll.OnDrag(move(-20));
        }

        scroll.OnDragEnd(press());

        Assert.That(restingScroll(), Is.GreaterThan(start + 100 + 50));
    }
}
