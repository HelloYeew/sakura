// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Numerics;
using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Cursor;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Input;
using Sakura.Framework.Testing;
using Sakura.Framework.Utilities;
using Vector2 = Sakura.Framework.Maths.Vector2;

namespace Sakura.Framework.Tests.Visuals.UserInterface;

public partial class TestBasicSliderBar : ManualInputManagerTestScene
{
    private BasicSliderBar<float> slider;
    private SpriteText valueText;

    [SetUp]
    public void SetUp()
    {
        AddStep("Add tooltip container", () => InputManager.Add(new TooltipContainer()));

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

        // Default KeyboardStep is 1 (absolute unit), which happens to equal 1% of this
        // slider's 0-100 range - that's a coincidence of the range width, not the mechanism.
        AddStep("Press Right arrow", () => InputManager.PressKey(Key.Right));
        AddAssert("Value increased by KeyboardStep (1)", () => Precision.AlmostEquals(slider.Current.Value, 51f, 0.1f));

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

        // Ctrl multiplies KeyboardStep (1) by 10 -> 10, which again equals 10% only because
        // this slider's range happens to be 100 wide.
        AddStep("Press Ctrl+Right", () => InputManager.PressKey(Key.Right, KeyModifiers.Control));
        AddAssert("Value jumped by 10x KeyboardStep (10)", () => Precision.AlmostEquals(slider.Current.Value, 60f, 0.1f));

        AddStep("Press Ctrl+Left", () => InputManager.PressKey(Key.Left, KeyModifiers.Control));
        AddAssert("Value jumped back by 10x KeyboardStep (10)", () => Precision.AlmostEquals(slider.Current.Value, 50f, 0.1f));
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
        AddStep("Drive value continuously to 100", () =>
        {
            slider.Current.Value = 0f;

            for (int i = 1; i <= 10; i++)
                slider.Current.Value = i * 10f;

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

    [Test]
    public void TestKeyboardStepIsAbsoluteNotFractionOfRange()
    {
        BasicSliderBar<double> levelSlider = null!;

        AddStep("Add 0-255 slider with KeyboardStep = 1 (mirrors the Green level slider)", () =>
        {
            TestContent.Add(levelSlider = new BasicSliderBar<double>
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Position = new Vector2(0, 100),
                Size = new Vector2(220, 18),
                MinValue = 0,
                MaxValue = 255,
                KeyboardStep = 1
            });
        });

        AddStep("Click slider to focus", () =>
        {
            InputManager.MoveMouseTo(levelSlider);
            InputManager.Click(MouseButton.Left);
        });

        AddStep("Set value to 0", () => levelSlider.Current.Value = 0);
        AddStep("Press Right arrow once", () => InputManager.PressKey(Key.Right));
        AddAssert("Value moved by exactly 1, not by the whole 255-wide range", () => levelSlider.Current.Value == 1);

        AddStep("Press Right arrow 9 more times", () =>
        {
            for (int i = 0; i < 9; i++)
                InputManager.PressKey(Key.Right);
        });
        AddAssert("Value is 10 after 10 total presses of step 1", () => levelSlider.Current.Value == 10);

        AddStep("Press Ctrl+Right once", () => InputManager.PressKey(Key.Right, KeyModifiers.Control));
        AddAssert("Ctrl+Right moves by 10x KeyboardStep (10)", () => levelSlider.Current.Value == 20);
    }

    [Test]
    public void TestDirectAssignmentClampsToBounds()
    {
        AddStep("Set value far above MaxValue directly", () => slider.Current.Value = 9999f);
        AddAssert("Value clamped to MaxValue", () => slider.Current.Value == slider.MaxValue);

        AddStep("Set value far below MinValue directly", () => slider.Current.Value = -9999f);
        AddAssert("Value clamped to MinValue", () => slider.Current.Value == slider.MinValue);
    }

    [Test]
    public void TestNarrowingBoundsReClampsCurrentValue()
    {
        AddStep("Set value to 80", () => slider.Current.Value = 80f);
        AddStep("Lower MaxValue below the current value", () => slider.MaxValue = 50f);
        AddAssert("Value re-clamped down to the new MaxValue", () => slider.Current.Value == 50f);

        AddStep("Set value to 10 (still within [0, 50])", () => slider.Current.Value = 10f);
        AddStep("Raise MinValue above the current value", () => slider.MinValue = 30f);
        AddAssert("Value re-clamped up to the new MinValue", () => slider.Current.Value == 30f);

        AddStep("Restore original bounds", () =>
        {
            slider.MinValue = 0f;
            slider.MaxValue = 100f;
        });
    }

    [Test]
    public void TestOutOfRangeDefaultValueClampsOnConstruction()
    {
        BasicSliderBar<double> areaSlider = null!;

        AddStep("Add slider whose default value (0) starts below MinValue (1)", () =>
        {
            TestContent.Add(areaSlider = new BasicSliderBar<double>
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Position = new Vector2(0, 130),
                Size = new Vector2(220, 18),
                MinValue = 1,
                MaxValue = 100
            });
        });

        AddAssert("Current value was clamped up to MinValue immediately, not left at 0", () => Precision.AlmostEquals(areaSlider.Current.Value, 1));
    }

    [Test]
    public void TestSymmetricRangeFillStartsAtMiddleWithoutInteraction()
    {
        BasicSliderBar<float> symmetricSlider = null!;

        AddStep("Add slider [-80, 80] - default value 0 doesn't need to change to satisfy that range", () =>
        {
            TestContent.Add(symmetricSlider = new BasicSliderBar<float>
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Position = new Vector2(0, 160),
                Size = new Vector2(200, 18),
                MinValue = -80f,
                MaxValue = 80f
            });
        });

        AddWaitStep("Let the slider finish loading", 100);

        AddAssert("Value is 0", () => symmetricSlider.Current.Value == 0f);
        AddAssert("Fill starts at the middle of the track", () => Precision.AlmostEquals(symmetricSlider.CurrentFillWidth, 0.5f, 0.01f));
    }

    private void runKeyboardStepAndClampingCase<T>(string typeLabel, T min, T max, T step, T outOfRangeLow, T outOfRangeHigh)
        where T : struct, INumber<T>, IMinMaxValue<T>
    {
        BasicSliderBar<T> numSlider = null!;

        AddStep($"Add {typeLabel} slider [{min}, {max}] with KeyboardStep = {step}", () =>
        {
            TestContent.Add(numSlider = new BasicSliderBar<T>
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Position = new Vector2(0, 160),
                Size = new Vector2(220, 18),
                MinValue = min,
                MaxValue = max,
                KeyboardStep = step
            });
        });

        AddStep("Click slider to focus", () =>
        {
            InputManager.MoveMouseTo(numSlider);
            InputManager.Click(MouseButton.Left);
        });

        AddStep("Set value to MinValue", () => numSlider.Current.Value = min);
        AddStep("Press Right arrow once", () => InputManager.PressKey(Key.Right));
        AddAssert("Value moved by exactly KeyboardStep", () => numSlider.Current.Value == min + step);

        AddStep("Press Home", () => InputManager.PressKey(Key.Home));
        AddStep("Press Left arrow at MinValue", () => InputManager.PressKey(Key.Left));
        AddAssert("Value stays at MinValue (clamped, not wrapped/underflowed)", () => numSlider.Current.Value == min);

        AddStep("Press End", () => InputManager.PressKey(Key.End));
        AddStep("Press Right arrow at MaxValue", () => InputManager.PressKey(Key.Right));
        AddAssert("Value stays at MaxValue (clamped, not overflowed)", () => numSlider.Current.Value == max);

        AddStep("Directly assign a value below MinValue", () => numSlider.Current.Value = outOfRangeLow);
        AddAssert("Out-of-range low assignment clamps to MinValue", () => numSlider.Current.Value == min);

        AddStep("Directly assign a value above MaxValue", () => numSlider.Current.Value = outOfRangeHigh);
        AddAssert("Out-of-range high assignment clamps to MaxValue", () => numSlider.Current.Value == max);
    }

    [Test]
    public void TestKeyboardStepAndClampingWithInt() =>
        runKeyboardStepAndClampingCase("int", 0, 255, 1, -50, 500);

    [Test]
    public void TestKeyboardStepAndClampingWithNarrowRangeInt() =>
        runKeyboardStepAndClampingCase("int (narrow range)", 0, 10, 3, -5, 20);

    [Test]
    public void TestKeyboardStepAndClampingWithFloat() =>
        runKeyboardStepAndClampingCase("float", 0f, 1f, 0.1f, -5f, 5f);

    [Test]
    public void TestKeyboardStepAndClampingWithSignedFloat() =>
        runKeyboardStepAndClampingCase("float (signed range)", -80f, 80f, 5f, -500f, 500f);

    [Test]
    public void TestKeyboardStepAndClampingWithDouble() =>
        runKeyboardStepAndClampingCase("double", 0.0, 255.0, 1.0, -100.0, 1000.0);

    [Test]
    public void TestKeyboardStepAndClampingWithLong() =>
        runKeyboardStepAndClampingCase("long", 0L, 1000L, 25L, -500L, 5000L);

    [Test]
    public void TestKeyboardStepAndClampingWithByte() =>
        runKeyboardStepAndClampingCase("byte", (byte)10, (byte)200, (byte)5, (byte)0, (byte)255);

    [Test]
    public void TestKeyboardStepAndClampingWithUInt() =>
        runKeyboardStepAndClampingCase("uint", (uint)10, (uint)200, (uint)5, (uint)0, (uint)9999);

    /// <summary>
    /// decimal can't be used as a [TestCase] attribute argument (not a valid C# attribute constant
    /// type), so it gets its own dedicated test rather than a case in the generic test above.
    /// </summary>
    [Test]
    public void TestKeyboardStepAndClampingWithDecimal()
    {
        BasicSliderBar<decimal> decimalSlider = null!;

        AddStep("Add decimal slider [0, 255] with KeyboardStep = 1", () =>
        {
            TestContent.Add(decimalSlider = new BasicSliderBar<decimal>
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Position = new Vector2(0, 190),
                Size = new Vector2(220, 18),
                MinValue = 0m,
                MaxValue = 255m,
                KeyboardStep = 1m
            });
        });

        AddStep("Click slider to focus", () =>
        {
            InputManager.MoveMouseTo(decimalSlider);
            InputManager.Click(MouseButton.Left);
        });

        AddStep("Set value to 0", () => decimalSlider.Current.Value = 0m);
        AddStep("Press Right arrow once", () => InputManager.PressKey(Key.Right));
        AddAssert("Value moved by exactly 1, not the whole range", () => decimalSlider.Current.Value == 1m);

        AddStep("Directly assign a value above range", () => decimalSlider.Current.Value = 9999m);
        AddAssert("Assignment above range clamps to MaxValue", () => decimalSlider.Current.Value == decimalSlider.MaxValue);

        AddStep("Directly assign a value below range", () => decimalSlider.Current.Value = -9999m);
        AddAssert("Assignment below range clamps to MinValue", () => decimalSlider.Current.Value == decimalSlider.MinValue);
    }

    private void clickSliderAt<T>(BasicSliderBar<T> target, float progress) where T : struct, INumber<T>, IMinMaxValue<T>
    {
        var position = new Vector2(
            target.DrawRectangle.X + target.DrawRectangle.Width * progress,
            target.DrawRectangle.Y + target.DrawRectangle.Height / 2f);

        InputManager.MoveMouseTo(position);
        InputManager.Click(MouseButton.Left);
    }

    [Test]
    public void TestNarrowIntRangeSnapsToEvenBuckets()
    {
        BasicSliderBar<int> narrowSlider = null!;

        AddStep("Add int slider [1, 3]", () =>
        {
            TestContent.Add(narrowSlider = new BasicSliderBar<int>
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Position = new Vector2(0, 220),
                Size = new Vector2(300, 18),
                MinValue = 1,
                MaxValue = 3
            });
        });

        AddStep("Drag to 10%", () => clickSliderAt(narrowSlider, 0.10f));
        AddAssert("Value is 1", () => narrowSlider.Current.Value == 1);

        AddStep("Drag to 40%", () => clickSliderAt(narrowSlider, 0.40f));
        AddAssert("Value is 2", () => narrowSlider.Current.Value == 2);

        AddStep("Drag to 60%", () => clickSliderAt(narrowSlider, 0.60f));
        AddAssert("Value is 2", () => narrowSlider.Current.Value == 2);

        AddStep("Drag to 90%", () => clickSliderAt(narrowSlider, 0.90f));
        AddAssert("Value is 3 without needing progress == 1.0 exactly", () => narrowSlider.Current.Value == 3);
    }

    [Test]
    public void TestStepSnapsToGrid()
    {
        BasicSliderBar<float> steppedSlider = null!;

        AddStep("Add float slider [0, 1] with Step = 0.25", () =>
        {
            TestContent.Add(steppedSlider = new BasicSliderBar<float>
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Position = new Vector2(0, 250),
                Size = new Vector2(200, 18),
                MinValue = 0f,
                MaxValue = 1f,
                Step = 0.25f
            });
        });

        AddStep("Drag to 10% (nearest grid point is 0)", () => clickSliderAt(steppedSlider, 0.10f));
        AddAssert("Value snapped to 0", () => Precision.AlmostEquals(steppedSlider.Current.Value, 0f, 0.001f));

        AddStep("Drag to 60% (nearest grid point is 0.5)", () => clickSliderAt(steppedSlider, 0.60f));
        AddAssert("Value snapped to 0.5", () => Precision.AlmostEquals(steppedSlider.Current.Value, 0.5f, 0.001f));

        AddStep("Drag to 90% (nearest grid point is 1.0)", () => clickSliderAt(steppedSlider, 0.90f));
        AddAssert("Value snapped to 1.0", () => Precision.AlmostEquals(steppedSlider.Current.Value, 1f, 0.001f));

        AddStep("Directly assign an off-grid value", () => steppedSlider.Current.Value = 0.32f);
        AddAssert("Direct assignment also snaps to the grid (0.25)", () => Precision.AlmostEquals(steppedSlider.Current.Value, 0.25f, 0.001f));
    }

    [Test]
    public void TestDecimalPlacesRoundsContinuousDragging()
    {
        BasicSliderBar<float> precisionSlider = null!;

        AddStep("Add float slider [0, 1] with DecimalPlaces = 2", () =>
        {
            TestContent.Add(precisionSlider = new BasicSliderBar<float>
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Position = new Vector2(0, 280),
                Size = new Vector2(200, 18),
                MinValue = 0f,
                MaxValue = 1f,
                DecimalPlaces = 2
            });
        });

        AddStep("Drag to a noisy, arbitrary progress", () => clickSliderAt(precisionSlider, 0.74827483f));
        AddAssert("Value is rounded to 2 decimal places", () =>
            Precision.AlmostEquals(precisionSlider.Current.Value, MathF.Round(precisionSlider.Current.Value, 2), 0.0001f));

        AddStep("Directly assign a noisy value", () => precisionSlider.Current.Value = 0.748274837f);
        AddAssert("Direct assignment is also rounded (0.748274837 -> 0.75)", () =>
            Precision.AlmostEquals(precisionSlider.Current.Value, 0.75f, 0.0001f));

        AddStep("Disable rounding", () => precisionSlider.DecimalPlaces = null);
        AddStep("Directly assign a noisy value again", () => precisionSlider.Current.Value = 0.123456f);
        AddAssert("Value is no longer rounded once DecimalPlaces is null", () =>
            Precision.AlmostEquals(precisionSlider.Current.Value, 0.123456f, 0.0001f));
    }

    [Test]
    public void TestDecimalPlacesIsNoOpForIntegerType()
    {
        BasicSliderBar<int> intSlider = null!;

        AddStep("Add int slider [0, 100] with DecimalPlaces = 2 (meaningless for whole numbers)", () =>
        {
            TestContent.Add(intSlider = new BasicSliderBar<int>
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Position = new Vector2(0, 100),
                Size = new Vector2(200, 18),
                MinValue = 0,
                MaxValue = 100,
                DecimalPlaces = 2
            });
        });

        AddStep("Set value to 42", () => intSlider.Current.Value = 42);
        AddAssert("Value is unaffected", () => intSlider.Current.Value == 42);
    }

    private partial class HoverTrackingSlider : BasicSliderBar<float>
    {
        public int HoverCount;
        public int HoverLostCount;

        protected override void OnHovered()
        {
            HoverCount++;
            base.OnHovered();
        }

        protected override void OnHoverLost()
        {
            HoverLostCount++;
            base.OnHoverLost();
        }
    }

    [Test]
    public void TestHoverState()
    {
        HoverTrackingSlider hoverSlider = null!;

        AddStep("Add hover-tracking slider", () =>
        {
            TestContent.Add(hoverSlider = new HoverTrackingSlider
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Position = new Vector2(0, 130),
                Size = new Vector2(200, 18),
                MinValue = 0f,
                MaxValue = 1f
            });
        });

        AddWaitStep("Let the slider finish loading", 100);

        AddStep("Move mouse away first", () => InputManager.MoveMouseTo(new Vector2(10, 10)));
        AddAssert("Not hovered", () => !hoverSlider.IsHovered);

        AddStep("Move mouse onto slider", () => InputManager.MoveMouseTo(hoverSlider));
        AddWaitStep("Let hover reconciliation settle", 50);
        AddAssert("Is hovered", () => hoverSlider.IsHovered);
        AddAssert("OnHovered fired once", () => hoverSlider.HoverCount == 1);

        AddStep("Move mouse away", () => InputManager.MoveMouseTo(new Vector2(10, 10)));
        AddWaitStep("Let hover reconciliation settle", 50);
        AddAssert("No longer hovered", () => !hoverSlider.IsHovered);
        AddAssert("OnHoverLost fired once", () => hoverSlider.HoverLostCount == 1);
    }

    [Test]
    public void TestDisabledSliderIgnoresInput()
    {
        BasicSliderBar<float> disabledSlider = null!;

        AddStep("Add disabled slider", () =>
        {
            TestContent.Add(disabledSlider = new BasicSliderBar<float>
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Position = new Vector2(0, 160),
                Size = new Vector2(200, 18),
                MinValue = 0f,
                MaxValue = 100f
            });

            disabledSlider.Enabled.Value = false;
        });

        AddWaitStep("Let the slider finish loading", 100);

        AddStep("Click center", () =>
        {
            InputManager.MoveMouseTo(disabledSlider);
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("Value unaffected by click", () => disabledSlider.Current.Value == 0f);
        AddAssert("Disabled slider does not take focus", () => !disabledSlider.HasFocus);

        AddStep("Try dragging across the whole track", () =>
        {
            InputManager.MoveMouseTo(disabledSlider);
            InputManager.PressButton(MouseButton.Left);
            InputManager.MoveMouseTo(new Vector2(
                disabledSlider.DrawRectangle.X + disabledSlider.DrawRectangle.Width,
                disabledSlider.DrawRectangle.Y + 10));
            InputManager.ReleaseButton(MouseButton.Left);
        });
        AddAssert("Value still unaffected by drag", () => disabledSlider.Current.Value == 0f);

        AddStep("Re-enable and click", () =>
        {
            disabledSlider.Enabled.Value = true;
            InputManager.MoveMouseTo(disabledSlider);
            InputManager.Click(MouseButton.Left);
        });
        AddWaitStep("Let the re-enable and click settle", 50);
        AddAssert("Value now roughly 50 after re-enabling", () => Precision.AlmostEquals(disabledSlider.Current.Value, 50f, 2f));
    }

    [Test]
    public void TestDoubleClickResetsToDefaultValue()
    {
        BasicSliderBar<float> resettableSlider = null!;

        AddStep("Add slider with DefaultValue = 25", () =>
        {
            TestContent.Add(resettableSlider = new BasicSliderBar<float>
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Position = new Vector2(0, 190),
                Size = new Vector2(200, 18),
                MinValue = 0f,
                MaxValue = 100f,
                DefaultValue = 25f
            });
        });

        AddWaitStep("Let the slider finish loading", 100);

        AddStep("Drag value away from default", () => clickSliderAt(resettableSlider, 0.9f));
        AddAssert("Value is roughly 90, not 25", () => !Precision.AlmostEquals(resettableSlider.Current.Value, 25f, 1f));

        AddStep("Double-click the slider", () =>
        {
            InputManager.MoveMouseTo(resettableSlider);
            InputManager.DoubleClick(MouseButton.Left);
        });
        AddWaitStep("Let the double-click settle", 50);
        AddAssert("Value reset to DefaultValue (25)", () => Precision.AlmostEquals(resettableSlider.Current.Value, 25f, 0.01f));
    }

    [Test]
    public void TestDoubleClickWithoutDefaultValueActsAsNormalClick()
    {
        BasicSliderBar<float> noDefaultSlider = null!;

        AddStep("Add slider without DefaultValue set", () =>
        {
            TestContent.Add(noDefaultSlider = new BasicSliderBar<float>
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Position = new Vector2(0, 220),
                Size = new Vector2(200, 18),
                MinValue = 0f,
                MaxValue = 100f
            });
        });

        AddWaitStep("Let the slider finish loading", 100);

        AddStep("Move mouse to 3/4 and double-click", () =>
        {
            InputManager.MoveMouseTo(new Vector2(
                noDefaultSlider.DrawRectangle.X + noDefaultSlider.DrawRectangle.Width * 0.75f,
                noDefaultSlider.DrawRectangle.Y + 10));
            InputManager.DoubleClick(MouseButton.Left);
        });
        AddWaitStep("Let the double-click settle", 50);
        AddAssert("Value moved to click position like a normal click", () => Precision.AlmostEquals(noDefaultSlider.Current.Value, 75f, 2f));
    }

    [Test]
    public void TestTooltipTextReflectsCurrentValue()
    {
        BasicSliderBar<float> tooltipSlider = null!;

        AddStep("Add slider with DecimalPlaces = 1", () =>
        {
            TestContent.Add(tooltipSlider = new BasicSliderBar<float>
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Position = new Vector2(0, 250),
                Size = new Vector2(200, 18),
                MinValue = 0f,
                MaxValue = 1f,
                DecimalPlaces = 1
            });
        });

        AddStep("Set value to a noisy float", () => tooltipSlider.Current.Value = 0.748274837f);
        AddAssert("TooltipText shows the value formatted to 1 decimal place", () =>
            ((IHasTooltip)tooltipSlider).TooltipText == tooltipSlider.Current.Value.ToString("F1", null));

        AddStep("Hover the slider", () => InputManager.MoveMouseTo(tooltipSlider));
        AddWaitStep("Wait for tooltip popup to appear", 400);
    }
}
