// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing.Input;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Input;

/// <summary>
/// Fingers either go to a drawable that takes them, or, for the first finger of a gesture, stand in for the
/// mouse the way a touchscreen does: a tap clicks on lift, and a finger that moves on scrolls what it's in
/// rather than pressing what it landed on.
/// </summary>
[TestFixture]
public class TouchInputTest
{
    private ManualClock manual = null!;
    private Container root = null!;
    private ManualInputManager input = null!;

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

        root.Add(input = new ManualInputManager { UseParentInput = false });

        root.Load();
        root.CompleteLoad();
    }

    private void settle(int frames = 3)
    {
        for (int i = 0; i < frames; i++)
        {
            manual.CurrentTime += 16;
            root.UpdateSubTree();
        }
    }

    private static Vector2 centreOf(Drawable drawable) => drawable.DrawRectangle.Location + drawable.DrawRectangle.Size / 2;

    [Test]
    public void TapClicksWhenTheFingerLifts()
    {
        var button = new Recorder { Position = new Vector2(100), Size = new Vector2(100) };
        input.Add(button);
        settle();

        input.TouchDown(TouchSource.Touch1, centreOf(button));
        Assert.That(button.Clicks, Is.Empty, "clicked when the finger landed");

        input.TouchUp(TouchSource.Touch1);
        Assert.That(button.Clicks, Is.EqualTo(new[] { 1 }));
    }

    [Test]
    public void WobbleWithinTheSlopIsStillATap()
    {
        var button = new Recorder { Position = new Vector2(100), Size = new Vector2(100) };
        input.Add(button);
        settle();

        input.TouchDown(TouchSource.Touch1, centreOf(button));
        input.MoveTouchTo(TouchSource.Touch1, centreOf(button) + new Vector2(TouchMouseEmulation.SLOP / 2, 0));
        input.TouchUp(TouchSource.Touch1);

        Assert.That(button.Clicks, Is.EqualTo(new[] { 1 }));
    }

    [Test]
    public void CancelledFingerClicksNothing()
    {
        var button = new Recorder { Position = new Vector2(100), Size = new Vector2(100) };
        input.Add(button);
        settle();

        input.TouchDown(TouchSource.Touch1, centreOf(button));
        input.TouchUp(TouchSource.Touch1, cancelled: true);

        Assert.That(button.Clicks, Is.Empty);
    }

    [Test]
    public void QuickSecondTapIsADoubleClick()
    {
        var button = new Recorder { Position = new Vector2(100), Size = new Vector2(100) };
        input.Add(button);
        settle();

        input.Tap(button);
        input.Tap(button);

        Assert.That(button.Clicks, Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    public void HeldFingerIsARightClickAndThenNothing()
    {
        var button = new Recorder { Position = new Vector2(100), Size = new Vector2(100) };
        input.Add(button);
        settle();

        input.TouchDown(TouchSource.Touch1, centreOf(button));
        settle(frames: (int)(TouchMouseEmulation.LONG_PRESS_MILLISECONDS / 16) + 2);

        Assert.That(button.RightPresses, Is.EqualTo(1), "holding the finger didn't right click");

        input.MoveTouchTo(TouchSource.Touch1, centreOf(button) + new Vector2(40, 0));
        input.TouchUp(TouchSource.Touch1);

        Assert.Multiple(() =>
        {
            Assert.That(button.Clicks, Is.Empty, "the finger clicked as it lifted after the long press");
            Assert.That(button.DragStarts, Is.Zero, "the finger dragged after the long press");
            Assert.That(button.IsHovered, Is.False);
        });
    }

    [Test]
    public void ShortHoldIsStillATap()
    {
        var button = new Recorder { Position = new Vector2(100), Size = new Vector2(100) };
        input.Add(button);
        settle();

        input.TouchDown(TouchSource.Touch1, centreOf(button));
        settle(frames: (int)(TouchMouseEmulation.LONG_PRESS_MILLISECONDS / 16) / 2);
        input.TouchUp(TouchSource.Touch1);

        Assert.Multiple(() =>
        {
            Assert.That(button.Clicks, Is.EqualTo(new[] { 1 }));
            Assert.That(button.RightPresses, Is.Zero);
        });
    }

    [Test]
    public void SecondFingerStopsTheFirstFromClicking()
    {
        var button = new Recorder { Position = new Vector2(100), Size = new Vector2(100) };
        input.Add(button);
        settle();

        // As a pinch starts: the first finger hasn't moved yet when the second lands.
        input.TouchDown(TouchSource.Touch1, centreOf(button));
        input.TouchDown(TouchSource.Touch2, new Vector2(500, 400));
        input.TouchUp(TouchSource.Touch1);
        input.TouchUp(TouchSource.Touch2);
        settle(frames: (int)(TouchMouseEmulation.LONG_PRESS_MILLISECONDS / 16) + 2);

        Assert.Multiple(() =>
        {
            Assert.That(button.Clicks, Is.Empty);
            Assert.That(button.RightPresses, Is.Zero);
        });
    }

    [Test]
    public void FlickStartingOnAButtonScrollsInsteadOfPressing()
    {
        Recorder button;
        ScrollableContainer scroll;

        input.Add(scroll = new ScrollableContainer
        {
            Size = new Vector2(200, 300),
            Children = new Drawable[]
            {
                button = new Recorder { Size = new Vector2(200, 100) },
                new Box { Y = 100, Size = new Vector2(200, 2000) },
            }
        });
        settle();

        var start = centreOf(button);
        input.TouchDown(TouchSource.Touch1, start);

        for (int i = 1; i <= 6; i++)
            input.MoveTouchTo(TouchSource.Touch1, start - new Vector2(0, 15 * i));

        input.TouchUp(TouchSource.Touch1);
        settle(30);

        Assert.Multiple(() =>
        {
            Assert.That(button.Clicks, Is.Empty, "the button the finger landed on was pressed");
            Assert.That(scroll.CurrentScroll.Y, Is.GreaterThan(50), "the container didn't scroll");
        });
    }

    [Test]
    public void NestedInputManagerSeesTheWholeGesture()
    {
        Recorder button;
        ScrollableContainer scroll;
        ManualInputManager nested;

        // As a test scene sits in the test browser, with its own input manager taking the real input.
        input.Add(nested = new ManualInputManager
        {
            UseParentInput = true,
            Child = scroll = new ScrollableContainer
            {
                Size = new Vector2(200, 300),
                Children = new Drawable[]
                {
                    button = new Recorder { Size = new Vector2(200, 100) },
                    new Box { Y = 100, Size = new Vector2(200, 2000) },
                }
            }
        });
        settle();

        var start = centreOf(button);
        input.TouchDown(TouchSource.Touch1, start);

        for (int i = 1; i <= 6; i++)
            input.MoveTouchTo(TouchSource.Touch1, start - new Vector2(0, 15 * i));

        input.TouchUp(TouchSource.Touch1);
        settle(30);

        Assert.Multiple(() =>
        {
            Assert.That(scroll.CurrentScroll.Y, Is.GreaterThan(50), "the container didn't scroll");
            Assert.That(button.Clicks, Is.Empty);
            Assert.That(nested.InputManager.HoveredDrawables, Is.Empty, "something stayed hovered in the nested manager");
            Assert.That(nested.InputManager.CurrentState.LastPointerWasTouch, Is.True);
        });
    }

    [Test]
    public void SidewaysFingerInAVerticalScrollDragsWhatItLandedOn()
    {
        Recorder slider;

        input.Add(new ScrollableContainer
        {
            Size = new Vector2(200, 300),
            Children = new Drawable[]
            {
                slider = new Recorder { Size = new Vector2(200, 100), TakesDrags = true },
                new Box { Y = 100, Size = new Vector2(200, 2000) },
            }
        });
        settle();

        var start = centreOf(slider);
        input.TouchDown(TouchSource.Touch1, start);
        input.MoveTouchTo(TouchSource.Touch1, start + new Vector2(40, 0));
        input.TouchUp(TouchSource.Touch1);

        Assert.Multiple(() =>
        {
            Assert.That(slider.DragStarts, Is.EqualTo(1));
            Assert.That(slider.DragDelta.X, Is.EqualTo(40).Within(0.01f), "the drag didn't follow the finger from where it landed");
            Assert.That(slider.DragEnds, Is.EqualTo(1));
        });
    }

    [Test]
    public void FingerDraggedOverAPanZoomBackgroundPans()
    {
        Box marker;
        input.Add(new PanZoomContainer
        {
            Size = new Vector2(800, 600),
            Child = marker = new Box { Position = new Vector2(500100), Size = new Vector2(20) }
        });
        settle();

        var before = marker.DrawRectangle.Location;

        input.TouchDown(TouchSource.Touch1, new Vector2(400, 300));

        for (int i = 1; i <= 5; i++)
            input.MoveTouchTo(TouchSource.Touch1, new Vector2(400 + 10 * i, 300));

        input.TouchUp(TouchSource.Touch1);
        settle();

        Assert.That(marker.DrawRectangle.Location.X - before.X, Is.EqualTo(50).Within(0.01f));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TwoFingersPinchAPanZoom(bool nested)
    {
        PanZoomContainer panZoom;
        Box marker;

        panZoom = new PanZoomContainer
        {
            Size = new Vector2(800, 600),
            Child = marker = new Box { Position = new Vector2(500300), Size = new Vector2(20) }
        };

        // Nested as a test scene's own input manager is: the first finger isn't taken, so only the outer manager
        // has it unless it passes it on.
        if (nested)
            input.Add(new ManualInputManager { UseParentInput = true, Child = panZoom });
        else
            input.Add(panZoom);

        settle();

        // The fingers start either side of the marker and spread to twice as far apart, which moves the point
        // between them 200 to the right.
        var markerStart = marker.DrawRectangle.Location;
        var midStart = markerStart + new Vector2(-30, 10);

        input.TouchDown(TouchSource.Touch1, midStart - new Vector2(100, 0));
        input.TouchDown(TouchSource.Touch2, midStart + new Vector2(100, 0));

        for (int i = 1; i <= 6; i++)
        {
            input.MoveTouchTo(TouchSource.Touch2, midStart + new Vector2(100 + 50 * i, 0));
            settle(1);
        }

        input.MoveTouchTo(TouchSource.Touch1, midStart);
        settle(1);

        var midEnd = midStart + new Vector2(200, 0);
        var expected = midEnd + (markerStart - midStart) * 2;

        Assert.Multiple(() =>
        {
            Assert.That(panZoom.Zoom, Is.EqualTo(2).Within(0.001f));
            Assert.That(marker.DrawRectangle.Location.X, Is.EqualTo(expected.X).Within(0.05f));
            Assert.That(marker.DrawRectangle.Location.Y, Is.EqualTo(expected.Y).Within(0.05f));
        });

        input.TouchUp(TouchSource.Touch1);
        input.TouchUp(TouchSource.Touch2);
        settle(frames: (int)(TouchMouseEmulation.LONG_PRESS_MILLISECONDS / 16) + 2);

        Assert.That(panZoom.Zoom, Is.EqualTo(2).Within(0.001f), "the pinch didn't end with its fingers");
    }

    [Test]
    public void NestedInputManagerHearsTheLiftOfAFingerItDidntTake()
    {
        ManualInputManager nested;
        input.Add(nested = new ManualInputManager { UseParentInput = true, Child = new Box { Size = new Vector2(200) } });
        settle();

        input.Tap(new Vector2(100));

        Assert.That(nested.InputManager.CurrentState.ActiveTouchCount, Is.Zero, "the nested manager still has a finger down");
    }

    [Test]
    public void SecondFingerIsNotTheMouse()
    {
        var button = new Recorder { Position = new Vector2(300, 100), Size = new Vector2(100) };
        input.Add(button);
        settle();

        input.TouchDown(TouchSource.Touch1, new Vector2(50, 50));
        input.Tap(centreOf(button), TouchSource.Touch2);
        input.TouchUp(TouchSource.Touch1);

        Assert.That(button.Clicks, Is.Empty);
    }

    [Test]
    public void TakenFingerFollowsItsDrawableAndIsNotTheMouse()
    {
        var taker = new Recorder { Position = new Vector2(100), Size = new Vector2(100), TakesTouches = true };
        var button = new Recorder { Position = new Vector2(400, 100), Size = new Vector2(100) };
        input.Add(taker);
        input.Add(button);
        settle();

        input.TouchDown(TouchSource.Touch1, centreOf(taker));
        input.MoveTouchTo(TouchSource.Touch1, centreOf(button));
        input.TouchUp(TouchSource.Touch1);

        Assert.Multiple(() =>
        {
            Assert.That(taker.Touches, Is.EqualTo(new[] { "down Touch1", "move Touch1", "up Touch1" }));
            Assert.That(button.Clicks, Is.Empty);
        });
    }

    [Test]
    public void EachFingerGoesToTheDrawableThatTookIt()
    {
        var left = new Recorder { Position = new Vector2(0, 100), Size = new Vector2(100), TakesTouches = true };
        var right = new Recorder { Position = new Vector2(200, 100), Size = new Vector2(100), TakesTouches = true };
        input.Add(left);
        input.Add(right);
        settle();

        input.TouchDown(TouchSource.Touch1, centreOf(left));
        input.TouchDown(TouchSource.Touch2, centreOf(right));
        input.MoveTouchTo(TouchSource.Touch2, centreOf(left));
        input.TouchUp(TouchSource.Touch1);
        input.TouchUp(TouchSource.Touch2);

        Assert.Multiple(() =>
        {
            Assert.That(left.Touches, Is.EqualTo(new[] { "down Touch1", "up Touch1" }));
            Assert.That(right.Touches, Is.EqualTo(new[] { "down Touch2", "move Touch2", "up Touch2" }));
        });
    }

    [Test]
    public void NothingStaysHoveredAfterATap()
    {
        var button = new Recorder { Position = new Vector2(100), Size = new Vector2(100) };
        input.Add(button);
        settle();

        input.Tap(button);

        Assert.Multiple(() =>
        {
            Assert.That(button.Hovers, Is.EqualTo(1), "the tap never hovered");
            Assert.That(button.IsHovered, Is.False);
            Assert.That(input.InputManager.CurrentState.LastPointerWasTouch, Is.True);
        });

        input.MoveMouseTo(new Vector2(10, 10));
        Assert.That(input.InputManager.CurrentState.LastPointerWasTouch, Is.False, "a real mouse didn't take over again");
    }

    [Test]
    public void TakenFingersMoveWithoutAllocating()
    {
        var taker = new Recorder { Position = new Vector2(100), Size = new Vector2(100), TakesTouches = true, Record = false };
        input.Add(taker);
        settle();

        input.TouchDown(TouchSource.Touch1, centreOf(taker));

        for (int i = 0; i < 10; i++)
            input.MoveTouchTo(TouchSource.Touch1, new Vector2(150 + i, 150));

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 100; i++)
            input.MoveTouchTo(TouchSource.Touch1, new Vector2(150 + i % 10, 150));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        input.TouchUp(TouchSource.Touch1);

        Assert.That(allocated, Is.Zero);
    }

    private partial class Recorder : Box
    {
        public bool TakesTouches;
        public bool TakesDrags;
        public bool Record = true;

        public readonly List<int> Clicks = new List<int>();
        public readonly List<string> Touches = new List<string>();
        public int Hovers;
        public int RightPresses;
        public int DragStarts;
        public int DragEnds;
        public Vector2 DragDelta;

        // Left clicks only: a right click is a press (counted in RightPresses) that also raises a click.
        public override bool OnClick(MouseButtonEvent e)
        {
            if (e.Button == MouseButton.Left)
                Clicks.Add(e.Clicks);

            return true;
        }

        public override bool OnDoubleClick(MouseButtonEvent e)
        {
            if (e.Button == MouseButton.Left)
                Clicks.Add(e.Clicks);

            return true;
        }

        public override bool OnMouseDown(MouseButtonEvent e)
        {
            if (e.Button == MouseButton.Right)
                RightPresses++;

            return base.OnMouseDown(e);
        }

        public override bool OnHover(MouseEvent e)
        {
            Hovers++;
            return false;
        }

        public override bool OnDragStart(MouseButtonEvent e)
        {
            DragStarts++;
            return TakesDrags;
        }

        public override bool OnDrag(MouseEvent e)
        {
            DragDelta += e.Delta;
            return true;
        }

        public override bool OnDragEnd(MouseButtonEvent e)
        {
            DragEnds++;
            return true;
        }

        public override bool OnTouchDown(TouchEvent e) => record("down", e) && TakesTouches;

        public override bool OnTouchMove(TouchEvent e) => record("move", e);

        public override bool OnTouchUp(TouchEvent e) => record("up", e);

        private bool record(string what, TouchEvent e)
        {
            if (Record && TakesTouches)
                Touches.Add($"{what} {e.Source}");

            return true;
        }
    }
}
