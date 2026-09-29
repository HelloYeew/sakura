// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Input;

public partial class TestFocusCharacterization : ManualInputManagerTestScene
{
    private FocusableBox first = null!;
    private FocusableBox second = null!;
    private Box plainBackground = null!;
    private RequestingBox overlay = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("Create focusable boxes and a plain background", () =>
        {
            TestContent.Clear();

            // A non-focusable background that still occupies space and can swallow clicks.
            TestContent.Add(plainBackground = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(1),
                Color = Color.DarkSlateGray,
                Alpha = 0.4f
            });

            TestContent.Add(first = new FocusableBox
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Position = new Vector2(-120, 0),
                Size = new Vector2(100),
                Color = Color.SteelBlue
            });

            TestContent.Add(second = new FocusableBox
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Position = new Vector2(120, 0),
                Size = new Vector2(100),
                Color = Color.IndianRed
            });

            // Stands in for a modal / focused overlay: unlike the plain boxes it REQUESTS focus, so it
            // is the only one suspended onto the focus stack when something else takes over.
            TestContent.Add(overlay = new RequestingBox
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Position = new Vector2(0, -150),
                Size = new Vector2(100),
                Color = Color.MediumPurple
            });
        });
    }

    [Test]
    public void TestClickAcquiresFocus()
    {
        AddAssert("Nothing focused initially", () => !first.HasFocus && !second.HasFocus);

        AddStep("Click first box", () =>
        {
            InputManager.MoveMouseTo(first);
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("First box gained focus", () => first.HasFocus);
        AddAssert("Second box not focused", () => !second.HasFocus);
    }

    [Test]
    public void TestClickThroughToNonFocusableReleasesFocus()
    {
        AddStep("Focus the first box", () =>
        {
            InputManager.MoveMouseTo(first);
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("First box focused", () => first.HasFocus);

        // The corner of the scene is covered only by the non-focusable background.
        AddStep("Click on the plain background", () =>
        {
            InputManager.MoveMouseTo(new Vector2(5, 5));
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("Focus released after clicking a non-focusable target", () => !first.HasFocus);
    }

    [Test]
    public void TestClickTransfersFocusBetweenFocusables()
    {
        AddStep("Focus first box", () =>
        {
            InputManager.MoveMouseTo(first);
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("First focused", () => first.HasFocus && !second.HasFocus);

        AddStep("Click second box", () =>
        {
            InputManager.MoveMouseTo(second);
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("Focus moved to second", () => second.HasFocus && !first.HasFocus);
    }

    [Test]
    public void TestFocusStackRestoresSuspendedRequesterOnRemoval()
    {
        AddStep("Focus the overlay", () =>
        {
            InputManager.MoveMouseTo(overlay);
            InputManager.Click(MouseButton.Left);
        });
        AddStep("Focus second box (the overlay goes on the stack)", () =>
        {
            InputManager.MoveMouseTo(second);
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("Second focused", () => second.HasFocus);

        AddStep("Remove the focused (second) box", () => TestContent.Remove(second));
        // Released via a drawable still in the tree -- a detached one can no longer reach the focus manager.
        AddStep("Release focus from the now-removed drawable", () => first.ReleaseFocus());
        AddAssert("Focus restored to the overlay from the stack", () => overlay.HasFocus);
    }

    [Test]
    public void TestClickingAwayAfterMovingBetweenBoxesReleasesFocusEntirely()
    {
        AddStep("Click first box", () =>
        {
            InputManager.MoveMouseTo(first);
            InputManager.Click(MouseButton.Left);
        });
        AddStep("Click second box", () =>
        {
            InputManager.MoveMouseTo(second);
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("Second focused", () => second.HasFocus && !first.HasFocus);

        // The corner of the scene is covered only by the non-focusable background.
        AddStep("Click on the plain background", () =>
        {
            InputManager.MoveMouseTo(new Vector2(5, 5));
            InputManager.Click(MouseButton.Left);
        });

        // Regression: focus used to bounce back to the first box here, because every transfer
        // pushed the outgoing drawable onto the focus stack.
        AddAssert("Nothing focused after clicking away", () => !first.HasFocus && !second.HasFocus);
    }

    private partial class FocusableBox : Box
    {
        public override bool AcceptsFocus => true;

        public void ReleaseFocus() => GetContainingFocusManager()?.ChangeFocus(null);
    }

    /// <summary>
    /// A stand-in for a modal / focused overlay: it requests focus, so it is suspended onto
    /// the focus stack (and later restored) rather than simply dropped.
    /// </summary>
    private partial class RequestingBox : FocusableBox
    {
        public override bool RequestsFocus => true;
    }
}
