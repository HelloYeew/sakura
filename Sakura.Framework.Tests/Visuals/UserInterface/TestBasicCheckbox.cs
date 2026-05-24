// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Input;
using Sakura.Framework.Reactive;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.UserInterface;

public class TestBasicCheckbox : ManualInputManagerTestScene
{
    private BasicCheckbox checkbox;
    private SpriteText stateText;

    [SetUp]
    public void SetUp()
    {
        AddStep("Add state text", () => TestContent.Add(stateText = new SpriteText()
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Text = "Checkbox state: False",
            Color = Color.White
        }));

        AddStep("Add checkbox", () =>
        {
            checkbox = new BasicCheckbox()
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Text = "Enable setting"
            };

            // Bind the reactive property to our text display for visual verification
            checkbox.Current.ValueChanged += e =>
            {
                stateText.Text = $"Checkbox state: {e.NewValue}";
            };

            TestContent.Add(checkbox);
        });
    }

    [Test]
    public void TestToggle()
    {
        AddStep("Move mouse to checkbox", () => InputManager.MoveMouseTo(checkbox));

        AddStep("Click checkbox", () => InputManager.Click(MouseButton.Left));
        AddAssert("Checkbox is checked", () => checkbox.Current.Value == true);
        AddAssert("Text updated to True", () => stateText.Text == "Checkbox state: True");

        AddStep("Click checkbox again", () => InputManager.Click(MouseButton.Left));
        AddAssert("Checkbox is unchecked", () => checkbox.Current.Value == false);
        AddAssert("Text updated to False", () => stateText.Text == "Checkbox state: False");
    }

    [Test]
    public void TestDisable()
    {
        AddStep("Move mouse to checkbox", () => InputManager.MoveMouseTo(checkbox));

        AddStep("Disable checkbox", () => checkbox.Enabled.Value = false);

        AddStep("Try to click", () => InputManager.Click(MouseButton.Left));
        AddAssert("Checkbox is still unchecked", () => checkbox.Current.Value == false);

        AddStep("Enable checkbox back", () => checkbox.Enabled.Value = true);

        AddStep("Try to click again", () => InputManager.Click(MouseButton.Left));
        AddAssert("Checkbox is checked", () => checkbox.Current.Value == true);
    }

    [Test]
    public void TestUseExternalReactive()
    {
        var externalReactive = new ReactiveBool();

        AddStep("Bind external reactive to checkbox", () => checkbox.Current.BindTo(externalReactive));

        AddStep("Set external reactive to true", () => externalReactive.Value = true);
        AddAssert("Checkbox is checked", () => checkbox.Current.Value);

        AddStep("Set external reactive to false", () => externalReactive.Value = false);
        AddAssert("Checkbox is unchecked", () => checkbox.Current.Value == false);
    }
}
