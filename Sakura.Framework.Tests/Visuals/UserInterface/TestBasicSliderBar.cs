// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;
using Sakura.Framework.Utilities;

namespace Sakura.Framework.Tests.Visuals.UserInterface;

public partial class TestBasicSliderBar : ManualInputManagerTestScene
{
    private BasicSliderBar<float> slider;
    private SpriteText valueText;

    [SetUp]
    public void SetUp()
    {
        AddStep("Add text and slider", () =>
        {
            TestContent.Add(valueText = new SpriteText
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Text = "Value: 0",
                Color = Color.White,
                Size = new Vector2(200, 30),
            });

            TestContent.Add(slider = new BasicSliderBar<float>
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(200, 20),
                MinValue = 0,
                MaxValue = 100
            });

            slider.Current.ValueChanged += e => valueText.Text = $"Value: {e.NewValue:F1}";
        });
    }

    [Test]
    public void TestClicking()
    {
        AddStep("Move mouse to center of slider", () => InputManager.MoveMouseTo(slider));
        AddStep("Click center", () => InputManager.Click(MouseButton.Left));
        AddAssert("Value is roughly 50", () => Precision.AlmostEquals(slider.Current.Value, 50f, 2f));

        AddStep("Move mouse to start of slider", () =>
            InputManager.MoveMouseTo(new Vector2(slider.DrawRectangle.X, slider.DrawRectangle.Y + 10)));
        AddStep("Click start", () => InputManager.Click(MouseButton.Left));
        AddAssert("Value is 0", () => slider.Current.Value == 0f);
    }

    [Test]
    public void TestDragging()
    {
        AddStep("Move mouse to start of slider", () =>
            InputManager.MoveMouseTo(new Vector2(slider.DrawRectangle.X, slider.DrawRectangle.Y + 10)));

        AddStep("Press mouse", () => InputManager.PressButton(MouseButton.Left));
        AddStep("Drag to middle", () => InputManager.MoveMouseTo(slider));
        AddStep("Release mouse", () => InputManager.ReleaseButton(MouseButton.Left));

        AddAssert("Value is roughly 50", () => Precision.AlmostEquals(slider.Current.Value, 50f, 2f));
    }

    [Test]
    public void TestOutOfBoundsDragging()
    {
        AddStep("Move mouse to center of slider", () => InputManager.MoveMouseTo(slider));
        AddStep("Press mouse", () => InputManager.PressButton(MouseButton.Left));
        AddStep("Drag far right", () =>
            InputManager.MoveMouseTo(new Vector2(slider.DrawRectangle.X + slider.DrawRectangle.Width + 50, slider.DrawRectangle.Y + 10)));
        AddStep("Release mouse", () => InputManager.ReleaseButton(MouseButton.Left));

        AddAssert("Value is max", () => slider.Current.Value == slider.MaxValue);

        AddStep("Move mouse to right of slider", () => InputManager.MoveMouseTo(new Vector2(slider.DrawRectangle.X + slider.DrawRectangle.Width, slider.DrawRectangle.Y + 10)));
        AddStep("Press mouse again", () => InputManager.PressButton(MouseButton.Left));
        AddStep("Drag far left", () =>
            InputManager.MoveMouseTo(new Vector2(slider.DrawRectangle.X - 50, slider.DrawRectangle.Y + 10)));
        AddStep("Release mouse", () => InputManager.ReleaseButton(MouseButton.Left));

        AddAssert("Value is min", () => slider.Current.Value == slider.MinValue);
    }

    [Test]
    public void TestFocusOnClick()
    {
        AddStep("Click slider", () =>
        {
            InputManager.MoveMouseTo(slider);
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("Slider has focus", () => slider.HasFocus);
    }

    [Test]
    public void TestClickElsewhereUnfocuses()
    {
        AddStep("Click slider to focus", () =>
        {
            InputManager.MoveMouseTo(slider);
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("Slider has focus", () => slider.HasFocus);

        AddStep("Click outside slider", () =>
        {
            InputManager.MoveMouseTo(new Vector2(10, 10));
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("Slider lost focus", () => !slider.HasFocus);
    }

    [Test]
    public void TestArrowKeyNavigation()
    {
        AddStep("Click slider to focus", () =>
        {
            InputManager.MoveMouseTo(slider);
            InputManager.Click(MouseButton.Left);
        });
        AddStep("Set initial value to 50", () => slider.Current.Value = 50f);

        AddStep("Press Right arrow", () => InputManager.PressKey(Key.Right));
        AddAssert("Value increased by 1%", () => Precision.AlmostEquals(slider.Current.Value, 51f, 0.1f));

        AddStep("Press Left arrow", () => InputManager.PressKey(Key.Left));
        AddAssert("Value decreased back to 50", () => Precision.AlmostEquals(slider.Current.Value, 50f, 0.1f));

        AddStep("Press Up arrow", () => InputManager.PressKey(Key.Up));
        AddAssert("Up arrow increases value", () => Precision.AlmostEquals(slider.Current.Value, 51f, 0.1f));

        AddStep("Press Down arrow", () => InputManager.PressKey(Key.Down));
        AddAssert("Down arrow decreases value", () => Precision.AlmostEquals(slider.Current.Value, 50f, 0.1f));
    }

    [Test]
    public void TestCtrlArrowLargeStep()
    {
        AddStep("Click slider to focus", () =>
        {
            InputManager.MoveMouseTo(slider);
            InputManager.Click(MouseButton.Left);
        });
        AddStep("Set initial value to 50", () => slider.Current.Value = 50f);

        AddStep("Press Ctrl+Right", () => InputManager.PressKey(Key.Right, KeyModifiers.Control));
        AddAssert("Value jumped by 10%", () => Precision.AlmostEquals(slider.Current.Value, 60f, 0.1f));

        AddStep("Press Ctrl+Left", () => InputManager.PressKey(Key.Left, KeyModifiers.Control));
        AddAssert("Value jumped back by 10%", () => Precision.AlmostEquals(slider.Current.Value, 50f, 0.1f));
    }

    [Test]
    public void TestHomeAndEndKeys()
    {
        AddStep("Click slider to focus", () =>
        {
            InputManager.MoveMouseTo(slider);
            InputManager.Click(MouseButton.Left);
        });
        AddStep("Set initial value to 50", () => slider.Current.Value = 50f);

        AddStep("Press End", () => InputManager.PressKey(Key.End));
        AddAssert("Value is max", () => slider.Current.Value == slider.MaxValue);

        AddStep("Press Home", () => InputManager.PressKey(Key.Home));
        AddAssert("Value is min", () => slider.Current.Value == slider.MinValue);
    }

    [Test]
    public void TestArrowKeysClampsAtBounds()
    {
        AddStep("Click slider to focus", () =>
        {
            InputManager.MoveMouseTo(slider);
            InputManager.Click(MouseButton.Left);
        });

        AddStep("Set value to max", () => slider.Current.Value = slider.MaxValue);
        AddStep("Press Right at max", () => InputManager.PressKey(Key.Right));
        AddAssert("Value stays at max", () => slider.Current.Value == slider.MaxValue);

        AddStep("Set value to min", () => slider.Current.Value = slider.MinValue);
        AddStep("Press Left at min", () => InputManager.PressKey(Key.Left));
        AddAssert("Value stays at min", () => slider.Current.Value == slider.MinValue);
    }

    [Test]
    public void TestKeyboardNotActiveWithoutFocus()
    {
        AddStep("Set value to 50", () => slider.Current.Value = 50f);
        AddAssert("Slider has no focus", () => !slider.HasFocus);

        AddStep("Press Right without focus", () => InputManager.PressKey(Key.Right));
        AddAssert("Value unchanged without focus", () => Precision.AlmostEquals(slider.Current.Value, 50f, 0.01f));
    }

    [Test]
    public void TestSetSliderReactive()
    {
        AddStep("Set slider value to 75", () => slider.Current.Value = 75f);
    }

    [Test]
    public void TestTwoSlidersSameReactive()
    {
        BasicSliderBar<float> slider2 = null!;
        SpriteText valueText2 = null!;

        AddStep("Add second slider", () =>
        {
            TestContent.Add(slider2 = new BasicSliderBar<float>
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Position = new Vector2(0, 50),
                Size = new Vector2(200, 20),
                MinValue = 0,
                MaxValue = 100
            });

            TestContent.Add(valueText2 = new SpriteText
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Position = new Vector2(0, 40),
                Text = "Second value: 0",
                Color = Color.White,
                Size = new Vector2(200, 30),
            });

            slider2.Current.ValueChanged += e => valueText2.Text = $"Second value: {e.NewValue:F1}";
        });

        AddStep("Bind second slider to first slider's reactive", () =>
        {
            slider2.Current.BindTo(slider.Current);

        });

        AddStep("Move mouse to center of first slider", () => InputManager.MoveMouseTo(slider));
        AddStep("Click center", () => InputManager.Click(MouseButton.Left));
        AddAssert("First slider value is roughly 50", () => Precision.AlmostEquals(slider.Current.Value, 50f, 2f));
        AddAssert("Second slider value is roughly 50", () => Precision.AlmostEquals(slider2.Current.Value, 50f, 2f));
        AddStep("Move mouse to 3/4 of first slider", () => InputManager.MoveMouseTo(new Vector2(slider.DrawRectangle.X + slider.DrawRectangle.Width * 0.75f, slider.DrawRectangle.Y + 10)));
        AddStep("Click 3/4", () => InputManager.Click(MouseButton.Left));
        AddAssert("First slider value is roughly 75", () => Precision.AlmostEquals(slider.Current.Value, 75f, 2f));
        AddAssert("Second slider value is roughly 75", () => Precision.AlmostEquals(slider2.Current.Value, 75f, 2f));

        AddStep("Unbind second slider", () => slider2.Current.UnbindFrom(slider.Current));
        AddStep("Move mouse to 1/4 of first slider", () => InputManager.MoveMouseTo(new Vector2(slider.DrawRectangle.X + slider.DrawRectangle.Width * 0.25f, slider.DrawRectangle.Y + 10)));
        AddStep("Click 1/4", () => InputManager.Click(MouseButton.Left));
        AddAssert("First slider value is roughly 25", () => Precision.AlmostEquals(slider.Current.Value, 25f, 2f));
        AddAssert("Second slider value is still roughly 75", () => Precision.AlmostEquals(slider2.Current.Value, 75f, 2f));
    }

    [Test]
    public void TestContinuousDriveSnapsFill()
    {
        // Simulate a value driven repeatedly within the same frame (e.g. a slider tracking playback
        // position, updated every Update()). Successive changes arrive with ~zero gap, so the slider
        // should detect a continuous drive and snap to the latest target rather than easing.
        //
        // NOTE: driving across separate AddSteps would NOT reproduce this — the test runner leaves a
        // gap between steps, which the heuristic correctly reads as isolated changes. The drive must
        // happen within a single step to be same-frame.
        AddStep("Drive value continuously to 100", () =>
        {
            slider.Current.Value = 0f;

            for (int i = 1; i <= 10; i++)
                slider.Current.Value = i * 10f;

            // Snapped same-frame: the fill is already full, no transform pending. (An eased change
            // would leave selection size unchanged this frame, i.e. CurrentFillWidth still ~0.)
            Assert.That(slider.CurrentFillWidth, Is.EqualTo(1f).Within(0.01f), "continuous drive should snap the fill");
        });
    }

    [Test]
    public void TestIsolatedChangeAnimatesFill()
    {
        AddStep("Set up long linear fill animation", () =>
        {
            slider.FillAnimationDuration = 5000;
            slider.FillAnimationEasing = Easing.None;
        });

        AddStep("Set value to 0", () => slider.Current.Value = 0f);
        AddWaitStep("Let fill settle at 0", 200);

        AddStep("Jump value to 100 and check it eases", () =>
        {
            slider.Current.Value = 100f;
            Assert.That(slider.CurrentFillWidth, Is.LessThan(0.5f), "fill should still be animating, not snapped");
        });

        AddUntilStep("Fill eventually reaches full", () => Precision.AlmostEquals(slider.CurrentFillWidth, 1f, 0.02f));
    }
}
