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

[TestFixture]
public partial class TestBasicTextBox : ManualInputManagerTestScene
{
    private BasicTextBox textBox;
    private SpriteText valueTrackerText;

    [SetUp]
    public void SetUp()
    {
        AddStep("Clear previous and setup test components", () =>
        {
            TestContent.Clear();

            TestContent.Add(valueTrackerText = new SpriteText
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Text = "Current Reactive Value: ",
                Color = Color.White,
                Size = new Vector2(400, 30),
            });

            TestContent.Add(textBox = new BasicTextBox
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Width = 300,
                Height = 40
            });

            textBox.Text.ValueChanged += e => valueTrackerText.Text = $"Current Reactive Value: {e.NewValue}";
        });
    }

    [Test]
    public void TestClickToFocusAndType()
    {
        AddAssert("Initially textbox is empty", () => textBox.Text.Value == "");
        AddAssert("Textbox does not have focus initially", () => !textBox.HasFocus);

        AddStep("Move mouse to textbox", () => InputManager.MoveMouseTo(textBox));
        AddStep("Click textbox to focus", () => InputManager.Click(MouseButton.Left));
        AddAssert("Textbox successfully gained focus", () => textBox.HasFocus);

        AddStep("Type text 'Sakura'", () => InputManager.TypeText("Sakura"));
        AddAssert("Reactive value updated to 'Sakura'", () => textBox.Text.Value == "Sakura");

        AddStep("Type space and more text", () => InputManager.TypeText(" aaa"));
        AddAssert("Text updated", () => textBox.Text.Value == "Sakura aaa");
    }

    [Test]
    public void TestInlineEditingAndNavigation()
    {
        AddStep("Focus and type initial string", () =>
        {
            InputManager.MoveMouseTo(textBox);
            InputManager.Click(MouseButton.Left);
            InputManager.TypeText("Framework");
        });


        AddStep("Press Backspace", () => InputManager.PressKey(Key.BackSpace));
        AddAssert("Last letter removed", () => textBox.Text.Value == "Framewor");

        AddStep("Press Left Arrow 4 times", () =>
        {
            InputManager.PressKey(Key.Left);
            InputManager.PressKey(Key.Left);
            InputManager.PressKey(Key.Left);
            InputManager.PressKey(Key.Left);
        });

        AddStep("Type character 'k' inside string", () => InputManager.TypeText("k"));
        AddAssert("Character inserted inside layout", () => textBox.Text.Value == "Framkewor");

        AddStep("Press Delete", () => InputManager.PressKey(Key.Delete));
        AddAssert("Deleted character ahead of caret index", () => textBox.Text.Value == "Framkwor");
    }

    [Test]
    public void TestImeCompositionVisuals()
    {
        AddStep("Focus textbox", () =>
        {
            InputManager.MoveMouseTo(textBox);
            InputManager.Click(MouseButton.Left);
        });

        AddStep("Simulate active IME editing phase", () => InputManager.EditComposingText("sakura", 0, 6));
        AddAssert("Reactive string remains empty during composition", () => textBox.Text.Value == "");

        AddStep("Commit IME string", () => InputManager.TypeText("桜"));
        AddAssert("Reactive state committed string value", () => textBox.Text.Value == "桜");
    }

    [Test]
    public void TestKeyboardSelectionAndReplacement()
    {
        AddStep("Focus and type initial string", () =>
        {
            InputManager.MoveMouseTo(textBox);
            InputManager.Click(MouseButton.Left);
            InputManager.TypeText("Hello World");
        });

        AddStep("Press Home", () => InputManager.PressKey(Key.Home));
        AddStep("Select 'Hello' with Shift+Right", () =>
        {
            for (int i = 0; i < 5; i++)
                InputManager.PressKey(Key.Right, KeyModifiers.Shift);
        });

        AddStep("Type 'Sakura'", () => InputManager.TypeText("Sakura"));
        AddAssert("Text replaced selection", () => textBox.Text.Value == "Sakura World");

        AddStep("Press End", () => InputManager.PressKey(Key.End));
        AddStep("Select 'World' with Shift+Left", () =>
        {
            for (int i = 0; i < 5; i++)
                InputManager.PressKey(Key.Left, KeyModifiers.Shift);
        });

        AddStep("Press Delete", () => InputManager.PressKey(Key.Delete));
        AddAssert("Text is 'Sakura '", () => textBox.Text.Value == "Sakura ");
    }

    [Test]
    public void TestMouseSelectionAndClipboard()
    {
        AddStep("Focus and type", () =>
        {
            InputManager.MoveMouseTo(textBox);
            InputManager.Click(MouseButton.Left);
            InputManager.TypeText("Testing Framework");
        });

        AddStep("Double click textbox", () => InputManager.DoubleClick(MouseButton.Left));

        AddStep("Press Ctrl+X", () => InputManager.PressKey(Key.X, KeyModifiers.Control));
        AddAssert("Textbox is empty after cut", () => textBox.Text.Value == "");

        AddStep("Type 'New Text '", () => InputManager.TypeText("New Text "));

        AddStep("Press Ctrl+V", () => InputManager.PressKey(Key.V, KeyModifiers.Control));
        AddAssert("Text was pasted successfully", () => textBox.Text.Value == "New Text Testing Framework");

        if (IsVisualRunner)
        {
            AddStep("Drag to select 'New Text'", () =>
            {
                float startX = textBox.DrawRectangle.X + 5;
                float endX = textBox.DrawRectangle.X + 80;

                InputManager.Drag(
                    new Vector2(startX, textBox.DrawRectangle.Center.Y),
                    new Vector2(endX, textBox.DrawRectangle.Center.Y)
                );
            });

            AddStep("Press Backspace on drag selection", () => InputManager.PressKey(Key.BackSpace));
            AddAssert("Dragged text was deleted", () => !textBox.Text.Value.Contains("New Text"));
        }
    }
}
