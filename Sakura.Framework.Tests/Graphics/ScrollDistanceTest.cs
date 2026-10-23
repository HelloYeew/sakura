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
/// How far a scroll container moves its content on screen for a drag, a trackpad and a wheel, when it's drawn at
/// another size than its own units, as a phone's scaled-down layout is.
/// </summary>
[TestFixture]
public class ScrollDistanceTest
{
    private const float start = 5000;

    // Drawn at half its size: one screen pixel is two of its units.
    private const float scale = 0.5f;

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
            Scale = new Vector2(scale),
            Clock = new FramedClock(manual)
        };

        root.Add(scroll = new ScrollableContainer { RelativeSizeAxes = Axes.Both });
        scroll.Add(new Box { RelativeSizeAxes = Axes.X, Height = 20000 });

        root.Load();
        root.CompleteLoad();
        settle(3);

        scroll.CurrentScroll = new Vector2(0, start);
        settle(3);
    }

    private void settle(int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            manual.CurrentTime += 16;
            root.UpdateSubTree();
        }
    }

    private static readonly MouseState pointer = new MouseState { Position = new Vector2(200, 150) };

    [Test]
    public void DragMovesTheContentAsFarAsTheFingerOnScreen()
    {
        scroll.OnDragStart(new MouseButtonEvent(pointer, MouseButton.Left, 1));
        scroll.OnDrag(new MouseEvent(pointer, new Vector2(0, -100)));
        settle(60);

        // 100 screen pixels are 200 of the container's units.
        Assert.That(scroll.CurrentScroll.Y, Is.EqualTo(start + 100 / scale).Within(0.5f));
    }

    [Test]
    public void TrackpadMovesTheContentAsFarAsTheFingersAtOnce()
    {
        scroll.OnScroll(new ScrollEvent(pointer, new Vector2(0, -5), new Vector2(0, -50)));

        Assert.That(scroll.CurrentScroll.Y, Is.EqualTo(start + 50 / scale).Within(0.5f));
    }

    [Test]
    public void WheelNotchMovesTheSetDistanceInTheContainersUnits()
    {
        scroll.OnScroll(new ScrollEvent(pointer, new Vector2(0, -1)));
        settle(120);

        Assert.That(scroll.CurrentScroll.Y, Is.EqualTo(start + scroll.ScrollDistance).Within(0.5f));
    }
}
