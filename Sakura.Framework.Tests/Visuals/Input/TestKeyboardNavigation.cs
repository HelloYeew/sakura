// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Input;

/// <summary>
/// Tab / Shift+Tab traversal and Space / Enter activation across the built-in controls. Run it in the
/// test browser to watch the focus outline move between controls.
/// </summary>
public partial class TestKeyboardNavigation : ManualInputManagerTestScene
{
    private BasicButton firstButton = null!;
    private BasicCheckbox checkbox = null!;
    private BasicSliderBar<double> slider = null!;
    private BasicButton lastButton = null!;
    private BasicButton disabledButton = null!;

    private int firstButtonClicks;

    [SetUp]
    public void SetUp()
    {
        AddStep("Create a row of controls", () =>
        {
            TestContent.Clear();
            firstButtonClicks = 0;

            TestContent.Add(new FlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                Direction = FlowDirection.Vertical,
                Spacing = new Vector2(0, 12),
                Children = new Drawable[]
                {
                    firstButton = new BasicButton
                    {
                        Size = new Vector2(180, 32),
                        Text = "First",
                        Action = () => firstButtonClicks++
                    },
                    checkbox = new BasicCheckbox(),
                    slider = new BasicSliderBar<double>
                    {
                        Size = new Vector2(180, 20),
                        MinValue = 0,
                        MaxValue = 10,
                        KeyboardStep = 1,
                        Step = 1
                    },
                    // No Action, so Enabled stays false: it must be skipped by Tab entirely.
                    disabledButton = new BasicButton
                    {
                        Size = new Vector2(180, 32),
                        Text = "Disabled"
                    },
                    lastButton = new BasicButton
                    {
                        Size = new Vector2(180, 32),
                        Text = "Last",
                        Action = () => { }
                    }
                }
            });
        });
    }

    private void pressTab(bool shift = false) =>
        AddStep(shift ? "Press Shift+Tab" : "Press Tab", () =>
        {
            if (shift) InputManager.PressKey(Key.ShiftLeft);
            InputManager.PressKey(Key.Tab);
            InputManager.ReleaseKey(Key.Tab);
            if (shift) InputManager.ReleaseKey(Key.ShiftLeft);
        });

    [Test]
    public void TestTabWalksTheControlsInOrder()
    {
        pressTab();
        AddAssert("First button focused", () => firstButton.HasFocus);

        pressTab();
        AddAssert("Checkbox focused", () => checkbox.HasFocus);

        pressTab();
        AddAssert("Slider focused", () => slider.HasFocus);

        pressTab();
        AddAssert("Disabled button skipped, last button focused", () => lastButton.HasFocus && !disabledButton.HasFocus);
    }

    [Test]
    public void TestShiftTabWalksBackwards()
    {
        pressTab();
        pressTab();
        AddAssert("Checkbox focused", () => checkbox.HasFocus);

        pressTab(shift: true);
        AddAssert("Back on the first button", () => firstButton.HasFocus);
    }

    [Test]
    public void TestTabWrapsAround()
    {
        pressTab(shift: true);
        AddAssert("Shift+Tab from cold lands on the last stop", () => lastButton.HasFocus);

        pressTab();
        AddAssert("Tab wraps back to the first", () => firstButton.HasFocus);
    }

    [Test]
    public void TestSpaceActivatesFocusedButton()
    {
        pressTab();
        AddAssert("First button focused", () => firstButton.HasFocus);

        AddStep("Press Space", () =>
        {
            InputManager.PressKey(Key.Space);
            InputManager.ReleaseKey(Key.Space);
        });
        AddAssert("Button fired", () => firstButtonClicks == 1);
    }

    [Test]
    public void TestEnterActivatesFocusedButton()
    {
        pressTab();
        AddStep("Press Enter", () =>
        {
            InputManager.PressKey(Key.Enter);
            InputManager.ReleaseKey(Key.Enter);
        });
        AddAssert("Button fired", () => firstButtonClicks == 1);
    }

    [Test]
    public void TestSpaceTogglesFocusedCheckbox()
    {
        pressTab();
        pressTab();
        AddAssert("Checkbox focused", () => checkbox.HasFocus);
        AddAssert("Unchecked to begin with", () => !checkbox.Current.Value);

        AddStep("Press Space", () =>
        {
            InputManager.PressKey(Key.Space);
            InputManager.ReleaseKey(Key.Space);
        });
        AddAssert("Checkbox toggled on", () => checkbox.Current.Value);

        AddStep("Press Space again", () =>
        {
            InputManager.PressKey(Key.Space);
            InputManager.ReleaseKey(Key.Space);
        });
        AddAssert("Checkbox toggled back off", () => !checkbox.Current.Value);
    }

    [Test]
    public void TestArrowKeysAdjustFocusedSlider()
    {
        AddStep("Focus the slider", () =>
        {
            InputManager.MoveMouseTo(slider);
            InputManager.Click(MouseButton.Left);
        });

        AddStep("Set a known value", () => slider.Current.Value = 5);
        AddStep("Press Right", () =>
        {
            InputManager.PressKey(Key.Right);
            InputManager.ReleaseKey(Key.Right);
        });
        AddAssert("Slider stepped up", () => slider.Current.Value == 6);

        // The slider consumes arrows, but must not consume Tab -- focus still moves on.
        pressTab();
        AddAssert("Tab still leaves the slider", () => !slider.HasFocus);
    }
}
