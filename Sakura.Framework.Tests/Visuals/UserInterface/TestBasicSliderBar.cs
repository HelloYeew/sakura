// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;
using Sakura.Framework.Utilities;

namespace Sakura.Framework.Tests.Visuals.UserInterface;

public class TestBasicSliderBar : ManualInputManagerTestScene
{
    private BasicSliderBar slider;
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

            TestContent.Add(slider = new BasicSliderBar
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
    public void TestSetSliderReactive()
    {
        AddStep("Set slider value to 75", () => slider.Current.Value = 75f);
    }

    [Test]
    public void TestTwoSlidersSameReactive()
    {
        BasicSliderBar slider2 = null!;
        SpriteText valueText2 = null!;

        AddStep("Add second slider", () =>
        {
            TestContent.Add(slider2 = new BasicSliderBar
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
}
