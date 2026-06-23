// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Input;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Input;

[TestFixture]
public class InputPropagationCharacterizationTest
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

        root.Load();
        root.LoadComplete();
    }

    private void settle()
    {
        for (int i = 0; i < 3; i++)
        {
            manual.CurrentTime += 16;
            root.UpdateSubTree();
        }
    }

    private static MouseButtonEvent mouseDown(Vector2 position, MouseButton button = MouseButton.Left, int clicks = 1)
    {
        var state = new MouseState { Position = position };
        state.SetPressed(button, true);
        return new MouseButtonEvent(state, button, clicks);
    }

    private static MouseButtonEvent mouseUp(Vector2 position, MouseButton button = MouseButton.Left)
    {
        var state = new MouseState { Position = position };
        return new MouseButtonEvent(state, button, 1);
    }

    private static MouseEvent mouseMove(Vector2 position) => new MouseEvent(new MouseState { Position = position });

    private static ScrollEvent scroll(Vector2 position, Vector2 delta) => new ScrollEvent(new MouseState { Position = position }, delta);

    [Test]
    public void TestMouseDownHitsFrontMostOverlappingChild()
    {
        // Two boxes occupying the same area; the later-added one has the higher depth and is "in front".
        var back = new RecordingBox
        {
            Position = new Vector2(100, 100),
            Size = new Vector2(100),
            HandleMouseDown = true
        };
        var front = new RecordingBox
        {
            Position = new Vector2(100, 100),
            Size = new Vector2(100),
            HandleMouseDown = true
        };

        root.Add(back);
        root.Add(front);
        settle();

        root.OnMouseDown(mouseDown(new Vector2(150, 150)));

        Assert.Multiple(() =>
        {
            Assert.That(front.MouseDownCount, Is.EqualTo(1), "Front-most child receives the event first.");
            Assert.That(back.MouseDownCount, Is.EqualTo(0), "Once the front-most child handles it, the event stops.");
        });
    }

    [Test]
    public void TestMouseDownFallsThroughWhenFrontChildDoesNotHandle()
    {
        var back = new RecordingBox
        {
            Position = new Vector2(100, 100),
            Size = new Vector2(100),
            HandleMouseDown = true
        };
        var front = new RecordingBox
        {
            Position = new Vector2(100, 100),
            Size = new Vector2(100),
            HandleMouseDown = false
        };

        root.Add(back);
        root.Add(front);
        settle();

        root.OnMouseDown(mouseDown(new Vector2(150, 150)));

        Assert.Multiple(() =>
        {
            Assert.That(front.MouseDownCount, Is.EqualTo(1), "Front-most child is still asked first.");
            Assert.That(back.MouseDownCount, Is.EqualTo(1), "Unhandled events fall through to the next child behind.");
        });
    }

    [Test]
    public void TestMouseDownSkipsChildNotContainingPoint()
    {
        var left = new RecordingBox
        {
            Position = new Vector2(0, 0),
            Size = new Vector2(100),
            HandleMouseDown = true
        };
        var right = new RecordingBox
        {
            Position = new Vector2(200, 0),
            Size = new Vector2(100),
            HandleMouseDown = true
        };

        root.Add(left);
        root.Add(right);
        settle();

        // Click inside `right` only.
        root.OnMouseDown(mouseDown(new Vector2(250, 50)));

        Assert.Multiple(() =>
        {
            Assert.That(right.MouseDownCount, Is.EqualTo(1));
            Assert.That(left.MouseDownCount, Is.EqualTo(0), "Positional events skip children that do not contain the point.");
        });
    }

    [Test]
    public void TestScrollOnlyDispatchedToChildUnderCursor()
    {
        var left = new RecordingBox
        {
            Position = new Vector2(0, 0),
            Size = new Vector2(100),
            HandleScroll = true
        };
        var right = new RecordingBox
        {
            Position = new Vector2(200, 0),
            Size = new Vector2(100),
            HandleScroll = true
        };

        root.Add(left);
        root.Add(right);
        settle();

        root.OnScroll(scroll(new Vector2(50, 50), new Vector2(0, 1)));

        Assert.Multiple(() =>
        {
            Assert.That(left.ScrollCount, Is.EqualTo(1), "Scroll goes to the child the cursor is over.");
            Assert.That(right.ScrollCount, Is.EqualTo(0), "Scroll is not delivered to children the cursor is not over.");
        });
    }

    [Test]
    public void TestDragCaptureKeepsMovesOnInitialChildOutsideBounds()
    {
        var draggable = new RecordingBox
        {
            Position = new Vector2(0, 0),
            Size = new Vector2(100),
            HandleMouseDown = true, HandleDrag = true
        };
        var other = new RecordingBox
        {
            Position = new Vector2(200, 0),
            Size = new Vector2(100),
            HandleDrag = true
        };

        root.Add(draggable);
        root.Add(other);
        settle();

        // Begin drag inside draggable.
        root.OnMouseDown(mouseDown(new Vector2(50, 50)));
        // Move the cursor far away, over other and then outside both.
        root.OnMouseMove(mouseMove(new Vector2(250, 50)));
        root.OnMouseMove(mouseMove(new Vector2(500, 500)));

        Assert.Multiple(() =>
        {
            Assert.That(draggable.DragCount, Is.GreaterThanOrEqualTo(2), "The captured child keeps receiving moves even outside its bounds.");
            Assert.That(other.DragCount, Is.EqualTo(0), "A drag in progress is not handed to another child under the cursor.");
        });

        // The captured child also receives the terminating mouse-up.
        root.OnMouseUp(mouseUp(new Vector2(500, 500)));
        Assert.That(draggable.MouseUpCount, Is.EqualTo(1), "The captured child receives the concluding mouse-up.");
    }

    [Test]
    public void TestHoverEnterAndLeaveFollowTheCursor()
    {
        var box = new RecordingBox
        {
            Position = new Vector2(100, 100),
            Size = new Vector2(100)
        };
        root.Add(box);
        settle();

        Assert.That(box.IsHovered, Is.False);

        // Move onto the box.
        root.OnMouseMove(mouseMove(new Vector2(150, 150)));
        Assert.Multiple(() =>
        {
            Assert.That(box.IsHovered, Is.True);
            Assert.That(box.HoverCount, Is.EqualTo(1), "Hover fires once on enter.");
        });

        // Move within the box: no extra enter.
        root.OnMouseMove(mouseMove(new Vector2(160, 160)));
        Assert.That(box.HoverCount, Is.EqualTo(1), "Hover does not re-fire while staying inside.");

        // Move off the box.
        root.OnMouseMove(mouseMove(new Vector2(500, 500)));
        Assert.Multiple(() =>
        {
            Assert.That(box.IsHovered, Is.False);
            Assert.That(box.HoverLostCount, Is.EqualTo(1), "Hover-lost fires once on leave.");
        });
    }

    [Test]
    public void TestKeyDownPropagatesNonPositionally()
    {
        var offscreen = new RecordingBox
        {
            Position = new Vector2(700, 500),
            Size = new Vector2(50),
            HandleKeyDown = true
        };
        root.Add(offscreen);
        settle();

        bool handled = root.OnKeyDown(new KeyEvent(Key.A, KeyModifiers.None, false));

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.True);
            Assert.That(offscreen.KeyDownCount, Is.EqualTo(1), "Keyboard events reach handlers regardless of cursor position.");
        });
    }

    [Test]
    public void TestKeyDownStopsAtFirstHandlerThatConsumes()
    {
        var first = new RecordingBox
        {
            Size = new Vector2(10),
            HandleKeyDown = true
        };
        var second = new RecordingBox
        {
            Size = new Vector2(10),
            HandleKeyDown = true
        };

        root.Add(first);
        root.Add(second);
        settle();

        root.OnKeyDown(new KeyEvent(Key.A, KeyModifiers.None, false));

        Assert.Multiple(() =>
        {
            Assert.That(first.KeyDownCount, Is.EqualTo(1));
            Assert.That(second.KeyDownCount, Is.EqualTo(0), "Once a handler consumes the key, propagation stops.");
        });
    }

    private partial class RecordingBox : Box
    {
        public bool HandleMouseDown;
        public bool HandleScroll;
        public bool HandleKeyDown;
        public bool HandleDrag;

        public int MouseDownCount;
        public int MouseUpCount;
        public int ScrollCount;
        public int KeyDownCount;
        public int DragCount;
        public int HoverCount;
        public int HoverLostCount;

        public override bool OnMouseDown(MouseButtonEvent e)
        {
            MouseDownCount++;
            // Mirror the base behaviour (focus + drag-start) only when we mean to capture the drag.
            if (HandleDrag)
                base.OnMouseDown(e);
            return HandleMouseDown || HandleDrag;
        }

        public override bool OnDragStart(MouseButtonEvent e) => HandleDrag;

        public override bool OnDrag(MouseEvent e)
        {
            DragCount++;
            return HandleDrag;
        }

        public override bool OnMouseUp(MouseButtonEvent e)
        {
            MouseUpCount++;
            return base.OnMouseUp(e);
        }

        public override bool OnScroll(ScrollEvent e)
        {
            ScrollCount++;
            return HandleScroll;
        }

        public override bool OnKeyDown(KeyEvent e)
        {
            KeyDownCount++;
            return HandleKeyDown;
        }

        public override bool OnHover(MouseEvent e)
        {
            HoverCount++;
            return false;
        }

        public override bool OnHoverLost(MouseEvent e)
        {
            HoverLostCount++;
            return false;
        }
    }
}
