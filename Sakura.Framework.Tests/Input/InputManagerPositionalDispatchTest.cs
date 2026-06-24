// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Input;

[TestFixture]
public class InputManagerPositionalDispatchTest
{
    private ManualClock manual = null!;
    private Container root = null!;
    private InputManager manager = null!;

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

        manager = new InputManager();
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
        => new MouseButtonEvent(new MouseState { Position = position }, button, 1);

    private static MouseEvent mouseMove(Vector2 position, Vector2 delta = default)
        => new MouseEvent(new MouseState { Position = position }, delta);

    private static ScrollEvent scroll(Vector2 position, Vector2 delta)
        => new ScrollEvent(new MouseState { Position = position }, delta);

    [Test]
    public void TestMouseDownHitsFrontMostOverlappingChild()
    {
        var back = new RecordingBox { Position = new Vector2(100, 100), Size = new Vector2(100), HandleMouseDown = true };
        var front = new RecordingBox { Position = new Vector2(100, 100), Size = new Vector2(100), HandleMouseDown = true };
        root.Add(back);
        root.Add(front);
        settle();

        manager.BuildQueues(root, new Vector2(150, 150));
        bool handled = manager.DispatchMouseDown(mouseDown(new Vector2(150, 150)));

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.True);
            Assert.That(front.MouseDownCount, Is.EqualTo(1), "Front-most child receives the event first.");
            Assert.That(back.MouseDownCount, Is.EqualTo(0), "Once the front-most child handles it, dispatch stops.");
            Assert.That(manager.DragCaptureTarget, Is.SameAs(front), "The handling child becomes the drag-capture target.");
        });
    }

    [Test]
    public void TestMouseDownFallsThroughWhenFrontDoesNotHandle()
    {
        var back = new RecordingBox { Position = new Vector2(100, 100), Size = new Vector2(100), HandleMouseDown = true };
        var front = new RecordingBox { Position = new Vector2(100, 100), Size = new Vector2(100), HandleMouseDown = false };
        root.Add(back);
        root.Add(front);
        settle();

        manager.BuildQueues(root, new Vector2(150, 150));
        manager.DispatchMouseDown(mouseDown(new Vector2(150, 150)));

        Assert.Multiple(() =>
        {
            Assert.That(front.MouseDownCount, Is.EqualTo(1), "Front-most child is asked first.");
            Assert.That(back.MouseDownCount, Is.EqualTo(1), "Unhandled events fall through to the child behind.");
        });
    }

    [Test]
    public void TestMouseDownSkipsChildNotUnderPoint()
    {
        var left = new RecordingBox { Position = new Vector2(0, 0), Size = new Vector2(100), HandleMouseDown = true };
        var right = new RecordingBox { Position = new Vector2(200, 0), Size = new Vector2(100), HandleMouseDown = true };
        root.Add(left);
        root.Add(right);
        settle();

        manager.BuildQueues(root, new Vector2(250, 50));
        manager.DispatchMouseDown(mouseDown(new Vector2(250, 50)));

        Assert.Multiple(() =>
        {
            Assert.That(right.MouseDownCount, Is.EqualTo(1));
            Assert.That(left.MouseDownCount, Is.EqualTo(0), "The positional queue excludes drawables the point misses.");
        });
    }

    [Test]
    public void TestScrollOnlyDispatchedToTargetUnderCursor()
    {
        var left = new RecordingBox { Position = new Vector2(0, 0), Size = new Vector2(100), HandleScroll = true };
        var right = new RecordingBox { Position = new Vector2(200, 0), Size = new Vector2(100), HandleScroll = true };
        root.Add(left);
        root.Add(right);
        settle();

        manager.BuildQueues(root, new Vector2(50, 50));
        manager.DispatchScroll(scroll(new Vector2(50, 50), new Vector2(0, 1)));

        Assert.Multiple(() =>
        {
            Assert.That(left.ScrollCount, Is.EqualTo(1), "Scroll goes to the target under the cursor.");
            Assert.That(right.ScrollCount, Is.EqualTo(0), "Scroll is not delivered to targets the cursor is not over.");
        });
    }

    [Test]
    public void TestDragCaptureKeepsMovesOnTargetOutsideBounds()
    {
        var draggable = new RecordingBox { Position = new Vector2(0, 0), Size = new Vector2(100), HandleMouseDown = true, HandleDrag = true };
        var other = new RecordingBox { Position = new Vector2(200, 0), Size = new Vector2(100), HandleDrag = true };
        root.Add(draggable);
        root.Add(other);
        settle();

        // Begin the drag inside `draggable`.
        manager.BuildQueues(root, new Vector2(50, 50));
        manager.DispatchMouseDown(mouseDown(new Vector2(50, 50)));
        Assert.That(manager.DragCaptureTarget, Is.SameAs(draggable));

        // Move over `other` and then far outside both — the captured target keeps the moves.
        manager.BuildQueues(root, new Vector2(250, 50));
        manager.DispatchMouseMove(mouseMove(new Vector2(250, 50), new Vector2(200, 0)));
        manager.BuildQueues(root, new Vector2(500, 500));
        manager.DispatchMouseMove(mouseMove(new Vector2(500, 500), new Vector2(250, 450)));

        Assert.Multiple(() =>
        {
            Assert.That(draggable.DragCount, Is.GreaterThanOrEqualTo(2), "The captured target keeps receiving moves outside its bounds.");
            Assert.That(other.DragCount, Is.EqualTo(0), "A drag in progress is not handed to another drawable under the cursor.");
        });

        // The captured target also receives the concluding mouse-up, and capture releases.
        bool upHandled = manager.DispatchMouseUp(mouseUp(new Vector2(500, 500)));
        Assert.Multiple(() =>
        {
            Assert.That(draggable.MouseUpCount, Is.EqualTo(1), "The captured target receives the terminating mouse-up.");
            Assert.That(manager.DragCaptureTarget, Is.Null, "Capture is released on mouse-up.");
            Assert.That(upHandled, Is.True);
        });
    }

    [Test]
    public void TestHoverEnterAndLeaveFollowCursor()
    {
        var box = new RecordingBox { Position = new Vector2(100, 100), Size = new Vector2(100) };
        root.Add(box);
        settle();

        Assert.That(box.IsHovered, Is.False);

        // Move onto the box.
        manager.BuildQueues(root, new Vector2(150, 150));
        manager.DispatchMouseMove(mouseMove(new Vector2(150, 150)));
        Assert.Multiple(() =>
        {
            Assert.That(box.IsHovered, Is.True);
            Assert.That(box.HoverCount, Is.EqualTo(1), "Hover fires once on enter.");
        });

        // Move within the box: no extra enter.
        manager.BuildQueues(root, new Vector2(160, 160));
        manager.DispatchMouseMove(mouseMove(new Vector2(160, 160)));
        Assert.That(box.HoverCount, Is.EqualTo(1), "Hover does not re-fire while staying inside.");

        // Move off the box.
        manager.BuildQueues(root, new Vector2(500, 500));
        manager.DispatchMouseMove(mouseMove(new Vector2(500, 500)));
        Assert.Multiple(() =>
        {
            Assert.That(box.IsHovered, Is.False);
            Assert.That(box.HoverLostCount, Is.EqualTo(1), "Hover-lost fires once on leave.");
        });
    }

    [Test]
    public void TestMouseUpWithoutDragGoesToFrontMostUnderPoint()
    {
        var box = new RecordingBox { Position = new Vector2(100, 100), Size = new Vector2(100), HandleMouseUp = true };
        root.Add(box);
        settle();

        // No prior mouse-down, so there is no capture; the up walks the queue.
        manager.BuildQueues(root, new Vector2(150, 150));
        bool handled = manager.DispatchMouseUp(mouseUp(new Vector2(150, 150)));

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.True);
            Assert.That(box.MouseUpCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void TestHoverMarksWholeAncestorStackWhenNothingBlocks()
    {
        // root -> outer (container) -> box. The cursor over the box should hover every drawable under
        // it, because by default OnHover returns false (nothing blocks) and ReceivePositionalInputAt
        // is Contains — so all three are in the positional queue and each is marked hovered.
        var outer = new HoverRecordingContainer { Position = new Vector2(100, 100), Size = new Vector2(200) };
        var box = new RecordingBox { RelativeSizeAxes = Axes.Both, Size = new Vector2(1) };
        outer.Add(box);
        root.Add(outer);
        settle();

        manager.BuildQueues(root, new Vector2(150, 150));
        manager.DispatchMouseMove(mouseMove(new Vector2(150, 150)));

        Assert.Multiple(() =>
        {
            Assert.That(box.IsHovered, Is.True, "The drawable directly under the cursor is hovered.");
            Assert.That(outer.IsHovered, Is.True, "Its containing parent is also hovered.");
            Assert.That(root.IsHovered, Is.True, "And the root, which contains every point, is hovered too.");
            Assert.That(manager.HoveredDrawables, Does.Contain(box));
            Assert.That(manager.HoveredDrawables, Does.Contain(outer));
            Assert.That(manager.HoveredDrawables, Does.Contain(root));
        });
    }

    [Test]
    public void TestHoverStopsAtBlockingAncestor()
    {
        // root -> blocker (OnHover returns true) -> box. Hover reaches the box and the blocker (front
        // of and including the blocker), but stops before the root behind it.
        var blocker = new HoverRecordingContainer { Position = new Vector2(100, 100), Size = new Vector2(200), BlockHover = true };
        var box = new RecordingBox { RelativeSizeAxes = Axes.Both, Size = new Vector2(1) };
        blocker.Add(box);
        root.Add(blocker);
        settle();

        manager.BuildQueues(root, new Vector2(150, 150));
        manager.DispatchMouseMove(mouseMove(new Vector2(150, 150)));

        Assert.Multiple(() =>
        {
            Assert.That(box.IsHovered, Is.True, "The drawable in front of the blocker is hovered.");
            Assert.That(blocker.IsHovered, Is.True, "The blocker itself is hovered.");
            Assert.That(root.IsHovered, Is.False, "Drawables behind a blocking ancestor are not hovered.");
            Assert.That(manager.HoveredDrawables, Does.Not.Contain(root));
        });
    }

    [Test]
    public void TestHoverStackClearedOnLeave()
    {
        var outer = new HoverRecordingContainer { Position = new Vector2(100, 100), Size = new Vector2(200) };
        var box = new RecordingBox { RelativeSizeAxes = Axes.Both, Size = new Vector2(1) };
        outer.Add(box);
        root.Add(outer);
        settle();

        manager.BuildQueues(root, new Vector2(150, 150));
        manager.DispatchMouseMove(mouseMove(new Vector2(150, 150)));
        Assert.That(box.IsHovered && outer.IsHovered, Is.True);

        // Move the cursor off the outer container (but still inside the root).
        manager.BuildQueues(root, new Vector2(500, 500));
        manager.DispatchMouseMove(mouseMove(new Vector2(500, 500)));

        Assert.Multiple(() =>
        {
            Assert.That(box.IsHovered, Is.False, "The box is no longer hovered after leaving.");
            Assert.That(outer.IsHovered, Is.False, "The outer container is no longer hovered after leaving.");
            Assert.That(box.HoverLostCount, Is.EqualTo(1), "OnHoverLost fired once for the box.");
            Assert.That(outer.HoverLostCount, Is.EqualTo(1), "OnHoverLost fired once for the outer container.");
            Assert.That(root.IsHovered, Is.True, "The root still contains the cursor, so it stays hovered.");
        });
    }

    private partial class RecordingBox : Box
    {
        public bool HandleMouseDown;
        public bool HandleMouseUp;
        public bool HandleScroll;
        public bool HandleDrag;

        public int MouseDownCount;
        public int MouseUpCount;
        public int ScrollCount;
        public int DragCount;
        public int HoverCount;
        public int HoverLostCount;

        public override bool OnMouseDown(MouseButtonEvent e)
        {
            MouseDownCount++;
            // Mirror base focus/drag-start only when capturing a drag.
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
            // Drive the base drag-end bookkeeping (clears IsDragged) when relevant.
            base.OnMouseUp(e);
            return HandleMouseUp || HandleDrag;
        }

        public override bool OnScroll(ScrollEvent e)
        {
            ScrollCount++;
            return HandleScroll;
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

    private partial class HoverRecordingContainer : Container
    {
        public bool BlockHover;
        public int HoverCount;
        public int HoverLostCount;

        public override bool OnHover(MouseEvent e)
        {
            HoverCount++;
            return BlockHover;
        }

        public override bool OnHoverLost(MouseEvent e)
        {
            HoverLostCount++;
            return false;
        }
    }
}
