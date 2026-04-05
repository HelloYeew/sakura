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

namespace Sakura.Framework.Tests.Visuals.UserInterface;

public class TestBasicButton : ManualInputManagerTestScene
{
    private BasicButton button;
    private int clickCount;
    private SpriteText clickCountText;

    [SetUp]
    public void SetUp()
    {
        AddStep("Reset click count", () => clickCount = 0);

        AddStep("Add test text", () => TestContent.Add(clickCountText = new SpriteText()
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Text = "Button clicked 0 times!",
            Color = Color.White,
            Size = new Vector2(200, 30),
        }));

        AddStep("Add button", () =>
        {
            button = new BasicButton()
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(200, 50),
                Text = "Click me!",
                Action = () =>
                {
                    clickCount++;
                    clickCountText.Text = $"Button clicked {clickCount} times!";
                }
            };
            TestContent.Add(button);
        });
    }

    [Test]
    public void TestClick()
    {
        AddStep("Move mouse to button", () => InputManager.MoveMouseTo(button));

        AddStep("Click button", () => InputManager.Click(MouseButton.Left));
        AddAssert("Click count is 1", () => clickCount == 1);

        AddStep("Click button again", () => InputManager.Click(MouseButton.Left));
        AddAssert("Click count is 2", () => clickCount == 2);

        AddStep("Drag test", () => InputManager.Drag(button, clickCountText, MouseButton.Left));
    }

    [Test]
    public void TestDisable()
    {
        AddStep("Move mouse to button", () => InputManager.MoveMouseTo(button));

        AddStep("Disable button", () => button.Enabled.Value = false);

        AddStep("Try to click", () => InputManager.Click(MouseButton.Left));
        AddAssert("Click count is still 0", () => clickCount == 0);

        AddStep("Enable button back", () => button.Enabled.Value = true);

        AddStep("Try to click again", () => InputManager.Click(MouseButton.Left));
        AddAssert("Click count is 1", () => clickCount == 1);
    }
}
