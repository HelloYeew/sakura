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
    public void TestFocusStackRestoresPreviousOnRemoval()
    {
        AddStep("Focus first box", () =>
        {
            InputManager.MoveMouseTo(first);
            InputManager.Click(MouseButton.Left);
        });
        AddStep("Focus second box (first goes on the stack)", () =>
        {
            InputManager.MoveMouseTo(second);
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("Second focused", () => second.HasFocus);

        AddStep("Remove the focused (second) box", () => TestContent.Remove(second));
        AddStep("Release focus from the now-removed drawable", () => first.ReleaseFocus());
        AddAssert("Focus restored to first box from the stack", () => first.HasFocus);
    }

    private partial class FocusableBox : Box
    {
        public override bool AcceptsFocus => true;

        public void ReleaseFocus() => GetContainingFocusManager()?.ChangeFocus(null);
    }
}
