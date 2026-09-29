// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Cursor;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Input;

public partial class TestCursorFeedback : ManualInputManagerTestScene
{
    private CursorProvider outer;
    private CursorProvider inner;
    private CursorProvider behindBlocker;
    private HoverBlocker blocker;
    private Container plain;

    [Resolved]
    private IWindow window { get; set; }

    /// <summary>
    /// The scene's <c>InputManager</c> property is the test harness's <c>ManualInputManager</c>,
    /// which owns the real one rather than being it.
    /// </summary>
    private Sakura.Framework.Input.InputManager realInputManager => InputManager.InputManager;

    private CursorState resolved => CursorFeedbackContainer.Resolve(realInputManager);

    [SetUp]
    public void SetUp()
    {
        AddStep("Build the scene", () =>
        {
            TestContent.Clear();

            TestContent.Add(new CursorFeedbackContainer());

            // A provider with a second provider nested inside it: the inner one is front-most and
            // must win, which is exactly the case that breaks when each control sets the cursor
            // itself (the outer's OnHoverLost fires on the way in and clears what the inner set).
            TestContent.Add(outer = new CursorProvider(CursorState.Crosshair)
            {
                Draggable = true,
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Position = new Vector2(20, 20),
                Size = new Vector2(200),
                Child = inner = new CursorProvider(CursorState.Pointer)
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(60),
                },
            });

            // A provider completely covered by something that blocks hover. The queue still holds
            // it; the hover set does not.
            TestContent.Add(new Container
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Position = new Vector2(260, 20),
                Size = new Vector2(200),
                Children = new Drawable[]
                {
                    behindBlocker = new CursorProvider(CursorState.Text) { RelativeSizeAxes = Axes.Both },
                    blocker = new HoverBlocker { RelativeSizeAxes = Axes.Both },
                },
            });

            // Somewhere with no provider at all.
            TestContent.Add(plain = new Container
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Position = new Vector2(20, 260),
                Size = new Vector2(200),
                Child = new Box { RelativeSizeAxes = Axes.Both, Color = Color.DimGray },
            });
        });
    }

    /// <summary>
    /// A point inside a drawable but clear of anything nested at its centre, which is where
    /// <c>MoveMouseTo(drawable)</c> would otherwise aim.
    /// </summary>
    private static Vector2 cornerOf(Drawable drawable) => drawable.ToScreenSpace(new Vector2(10, 10));

    [Test]
    public void TestNothingUnderThePointerIsDefault()
    {
        AddStep("Hover a plain container", () => InputManager.MoveMouseTo(plain));
        AddAssert("Default", () => resolved == CursorState.Default);
    }

    [Test]
    public void TestAProviderIsHonoured()
    {
        AddStep("Hover the outer provider", () => InputManager.MoveMouseTo(cornerOf(outer)));
        AddAssert("Crosshair", () => resolved == CursorState.Crosshair);
    }

    [Test]
    public void TestTheFrontMostProviderWins()
    {
        AddStep("Hover the nested provider", () => InputManager.MoveMouseTo(inner));

        AddAssert("Both are hovered", () => outer.IsHovered && inner.IsHovered);
        AddAssert("The inner one decides", () => resolved == CursorState.Pointer);
    }

    /// <summary>
    /// The queue holds everything under the pointer, including drawables behind an overlay that
    /// blocks hover. Only the hovered prefix may speak, or a control under an open menu would reach
    /// through it.
    /// </summary>
    [Test]
    public void TestAProviderBehindAHoverBlockerIsIgnored()
    {
        AddStep("Hover the blocker", () => InputManager.MoveMouseTo(blocker));

        AddAssert("The blocker is hovered", () => blocker.IsHovered);
        AddAssert("The provider behind it is not", () => !behindBlocker.IsHovered);
        AddAssert("So it does not get a say", () => resolved == CursorState.Default);
    }

    /// <summary>
    /// A drag holds capture after the pointer has left the drawable that started it — dragging an
    /// image past the cursor is the everyday case. The grip must stay closed.
    /// </summary>
    [Test]
    public void TestADragKeepsItsCursorAfterThePointerLeaves()
    {
        AddStep("Press on the outer provider", () =>
        {
            InputManager.MoveMouseTo(cornerOf(outer));
            InputManager.PressButton(MouseButton.Left);
        });

        AddStep("Drag right off it, onto the plain container", () => InputManager.MoveMouseTo(plain));

        AddAssert("The drag still holds capture", () => realInputManager.DragCaptureTarget == outer);
        AddAssert("And still decides the cursor", () => resolved == CursorState.Crosshair);

        AddStep("Release", () => InputManager.ReleaseButton(MouseButton.Left));
        AddAssert("Now the plain container decides", () => resolved == CursorState.Default);
    }

    [Test]
    public void TestAProviderCanChangeItsMind()
    {
        AddStep("Hover the outer provider", () => InputManager.MoveMouseTo(cornerOf(outer)));
        AddAssert("Crosshair", () => resolved == CursorState.Crosshair);

        AddStep("It changes its answer", () => outer.Cursor = CursorState.Wait);
        AddAssert("Read again, not cached", () => resolved == CursorState.Wait);
    }

    /// <summary>
    /// A container that states a cursor. Does not block hover, as most controls do not.
    /// </summary>
    private partial class CursorProvider : Container, IHasCursor
    {
        public CursorState Cursor { get; set; }

        /// <summary>
        /// Whether this one captures a drag. Capture requires <c>OnMouseDown</c> to return true, and
        /// a plain container returns false — so without this the drag rule cannot be exercised at
        /// all. Modelled on the real case, where a pannable sprite returns true from OnDragStart.
        /// </summary>
        public bool Draggable { get; init; }

        public CursorProvider(CursorState cursor)
        {
            Cursor = cursor;
            Add(new Box { RelativeSizeAxes = Axes.Both, Color = Color.SlateGray, Alpha = 0.5f });
        }

        public override bool OnDragStart(MouseButtonEvent e) => Draggable;

        public override bool OnDrag(MouseEvent e) => Draggable;
    }

    /// <summary>
    /// Stands in for an overlay: takes hover and stops it reaching anything behind.
    /// </summary>
    private partial class HoverBlocker : Container
    {
        public HoverBlocker()
        {
            Add(new Box { RelativeSizeAxes = Axes.Both, Color = Color.DarkSlateBlue, Alpha = 0.5f });
        }

        public override bool OnHover(MouseEvent e) => true;
    }
}
