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
    public void TestMouseMoveDeliveredToTargetUnderPointWithoutDrag()
    {
        // Regression test from https://github.com/HelloYeew/sakura/commit/230eb8602f875a0ae3b6e42df50f50e17a829aed
        // a plain mouse-move (no prior mouse-down, so no drag capture) must still be
        // delivered to the drawable under the cursor. Previously DispatchMouseMove only routed to the
        // drag-capture target, so a non-dragging drawable that overrides OnMouseMove (e.g. a nested
        // input manager relaying to its subtree) only ever saw a move *after* a click.
        var box = new RecordingBox { Position = new Vector2(100, 100), Size = new Vector2(100) };
        root.Add(box);
        settle();

        manager.BuildQueues(root, new Vector2(150, 150));
        manager.DispatchMouseMove(mouseMove(new Vector2(150, 150)));

        Assert.That(box.MouseMoveCount, Is.EqualTo(1),
            "The drawable under the cursor receives the move even with no drag in progress.");
    }

    [Test]
    public void TestMouseMoveStopsAtFirstConsumer()
    {
        // Front-most consumer claims the move; the one behind it does not also receive it.
        var back = new RecordingBox { Position = new Vector2(100, 100), Size = new Vector2(100), HandleMouseMove = true };
        var front = new RecordingBox { Position = new Vector2(100, 100), Size = new Vector2(100), HandleMouseMove = true };
        root.Add(back);
        root.Add(front);
        settle();

        manager.BuildQueues(root, new Vector2(150, 150));
        manager.DispatchMouseMove(mouseMove(new Vector2(150, 150)));

        Assert.Multiple(() =>
        {
            Assert.That(front.MouseMoveCount, Is.EqualTo(1), "Front-most consumer receives the move.");
            Assert.That(back.MouseMoveCount, Is.EqualTo(0), "A consumed move does not fall through.");
        });
    }

    [Test]
    public void TestMouseMoveDoesNotDriveHover()
    {
        // Regression test from https://github.com/HelloYeew/sakura/commit/230eb8602f875a0ae3b6e42df50f50e17a829aed
        // hover is owned solely by updateHover. Delivering a move must NOT toggle hover
        // through the base Drawable.OnMouseMove (which historically set IsHovered / fired OnHover).
        // A move-consumer that stops the walk early must not leave hover in a half-applied state, and
        // OnHover must be driven by the hover reconciliation, not the move delivery.
        var box = new RecordingBox { Position = new Vector2(100, 100), Size = new Vector2(100), HandleMouseMove = true };
        root.Add(box);
        settle();

        manager.BuildQueues(root, new Vector2(150, 150));
        manager.DispatchMouseMove(mouseMove(new Vector2(150, 150)));

        Assert.Multiple(() =>
        {
            // It is hovered — but via updateHover, which fires OnHover exactly once.
            Assert.That(box.IsHovered, Is.True);
            Assert.That(box.HoverCount, Is.EqualTo(1),
                "OnHover fired once via hover reconciliation, not additionally from OnMouseMove.");
            Assert.That(box.MouseMoveCount, Is.EqualTo(1), "The move was delivered.");
        });
    }

    [Test]
    public void TestMoveConsumerThatAlsoBlocksHoverIsIndependent()
    {
        // A front-most drawable that both consumes the move (stops the move walk) and blocks hover.
        // The two mechanisms are independent: the move stops at it, and hover stops at it, but a
        // drawable behind it receives neither.
        var back = new RecordingBox { Position = new Vector2(100, 100), Size = new Vector2(200), HandleMouseMove = true };
        var front = new BlockingMoveBox { Position = new Vector2(100, 100), Size = new Vector2(200) };
        root.Add(back);
        root.Add(front);
        settle();

        manager.BuildQueues(root, new Vector2(150, 150));
        manager.DispatchMouseMove(mouseMove(new Vector2(150, 150)));

        Assert.Multiple(() =>
        {
            Assert.That(front.MouseMoveCount, Is.EqualTo(1), "Front-most consumer receives and consumes the move.");
            Assert.That(back.MouseMoveCount, Is.EqualTo(0), "The move does not fall through to the drawable behind.");
            Assert.That(front.IsHovered, Is.True, "The blocker itself is hovered.");
            Assert.That(back.IsHovered, Is.False, "A drawable behind a hover blocker is not hovered.");
        });
    }

    [Test]
    public void TestRelayReceivesMoveWithoutPriorClick()
    {
        // Regression test from https://github.com/HelloYeew/sakura/commit/230eb8602f875a0ae3b6e42df50f50e17a829aed
        // a drawable that relays OnMouseMove to its own subtree (modelled
        // here by RelayBox, which records the move it relayed) must receive a plain move with no prior
        // mouse-down. Before the fix, DispatchMouseMove only routed to the drag-capture target, so a
        // relay only saw moves after a click made it the capture target.
        var relay = new RelayBox { Position = new Vector2(0, 0), Size = new Vector2(400) };
        root.Add(relay);
        settle();

        manager.BuildQueues(root, new Vector2(200, 200));
        manager.DispatchMouseMove(mouseMove(new Vector2(200, 200)));

        Assert.Multiple(() =>
        {
            Assert.That(relay.RelayedMoveCount, Is.EqualTo(1), "The relay received the move with no prior click.");
            Assert.That(relay.LastRelayedPosition, Is.EqualTo(new Vector2(200, 200)));
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
        var other = new RecordingBox { Position = new Vector2(200, 0), Size = new Vector2(100), HandleDrag = true, HandleMouseMove = true };
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
            Assert.That(other.MouseMoveCount, Is.EqualTo(0),
                "While a drag owns the move, the queue is not walked, so a drawable under the cursor gets no OnMouseMove.");
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
        public bool HandleMouseMove;

        public int MouseDownCount;
        public int MouseUpCount;
        public int ScrollCount;
        public int DragCount;
        public int MouseMoveCount;
        public int HoverCount;
        public int HoverLostCount;

        public override bool OnMouseMove(MouseEvent e)
        {
            MouseMoveCount++;

            // Preserve the base drag routing: when this box is the captured drag target, the base
            // OnMouseMove short-circuits to OnDrag. Without calling base, drag delivery would break.
            if (base.OnMouseMove(e))
                return true;

            return HandleMouseMove;
        }

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

    /// <summary>
    /// Consumes the move (stops the move walk) and blocks hover (OnHover returns true), to verify the
    /// two mechanisms are independent.
    /// </summary>
    private partial class BlockingMoveBox : RecordingBox
    {
        public BlockingMoveBox()
        {
            HandleMouseMove = true;
        }

        public override bool OnHover(MouseEvent e)
        {
            base.OnHover(e);
            return true;
        }
    }

    /// <summary>
    /// Models a drawable that relays plain mouse-moves to its own logic (like a nested input manager),
    /// recording each relayed move. Consumes the move so the relay is the authoritative handler.
    /// </summary>
    private partial class RelayBox : Box
    {
        public int RelayedMoveCount;
        public Vector2 LastRelayedPosition;

        public override bool OnMouseMove(MouseEvent e)
        {
            RelayedMoveCount++;
            LastRelayedPosition = e.ScreenSpaceMousePosition;
            return true;
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
