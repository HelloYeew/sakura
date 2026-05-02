// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

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
        AddAssert("Value is roughly 50", () => Math.Abs(slider.Current.Value - 50f) < 2f);

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

        AddAssert("Value is roughly 50", () => Math.Abs(slider.Current.Value - 50f) < 2f);
    }
}
