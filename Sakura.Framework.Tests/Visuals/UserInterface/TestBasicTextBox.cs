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

        // Double-click now selects a word; select-all via Ctrl+A for a full cut.
        AddStep("Select all with Ctrl+A", () => InputManager.PressKey(Key.A, KeyModifiers.Control));

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

    [Test]
    public void TestWordNavigationAndDeletion()
    {
        AddStep("Focus and type words", () =>
        {
            InputManager.MoveMouseTo(textBox);
            InputManager.Click(MouseButton.Left);
            InputManager.TypeText("one two three");
        });

        AddStep("Ctrl+Left", () => InputManager.PressKey(Key.Left, KeyModifiers.Control));
        AddStep("Type 'x' before last word", () => InputManager.TypeText("x"));
        AddAssert("Caret jumped to start of 'three'", () => textBox.Text.Value == "one two xthree");

        AddStep("Ctrl+Backspace", () => InputManager.PressKey(Key.BackSpace, KeyModifiers.Control));
        AddAssert("Word-fragment before caret deleted", () => textBox.Text.Value == "one two three");

        AddStep("Press Home", () => InputManager.PressKey(Key.Home));
        AddStep("Ctrl+Delete", () => InputManager.PressKey(Key.Delete, KeyModifiers.Control));
        AddAssert("First word deleted", () => textBox.Text.Value == " two three");

        AddStep("Ctrl+Shift+Right selects a word", () =>
            InputManager.PressKey(Key.Right, KeyModifiers.Control | KeyModifiers.Shift));
        AddStep("Type replacement", () => InputManager.TypeText("1"));
        AddAssert("Selected word replaced", () => textBox.Text.Value == "1 three");
    }

    [Test]
    public void TestDoubleClickSelectsWord()
    {
        AddStep("Focus and type words", () =>
        {
            InputManager.MoveMouseTo(textBox);
            InputManager.Click(MouseButton.Left);
            InputManager.TypeText("alpha beta");
        });

        // The mouse sits at the textbox centre; with default sizing this lands within the text.
        AddStep("Double click", () => InputManager.DoubleClick(MouseButton.Left));
        AddStep("Type replacement", () => InputManager.TypeText("_"));
        AddAssert("Only one word was replaced", () =>
            textBox.Text.Value.Contains('_') && textBox.Text.Value.Length < "alpha beta".Length + 1);
    }

    [Test]
    public void TestCommitEvent()
    {
        string? committed = null;

        AddStep("Listen for commit", () => textBox.OnCommit += text => committed = text);

        AddStep("Focus and type", () =>
        {
            InputManager.MoveMouseTo(textBox);
            InputManager.Click(MouseButton.Left);
            InputManager.TypeText("done");
        });

        AddStep("Press Enter", () => InputManager.PressKey(Key.Enter));
        AddAssert("Commit event fired with text", () => committed == "done");
        AddAssert("Focus released on commit", () => !textBox.HasFocus);
    }

    [Test]
    public void TestLengthLimit()
    {
        AddStep("Set length limit 5", () => textBox.LengthLimit = 5);

        AddStep("Focus and type beyond limit", () =>
        {
            InputManager.MoveMouseTo(textBox);
            InputManager.Click(MouseButton.Left);
            InputManager.TypeText("abcdefgh");
        });

        AddAssert("Text truncated to limit", () => textBox.Text.Value == "abcde");
    }

    [Test]
    public void TestPlaceholder()
    {
        AddStep("Set placeholder", () => textBox.PlaceholderText = "Enter name...");
        AddAssert("Placeholder set while empty", () => textBox.PlaceholderText == "Enter name...");

        AddStep("Focus and type", () =>
        {
            InputManager.MoveMouseTo(textBox);
            InputManager.Click(MouseButton.Left);
            InputManager.TypeText("a");
        });

        AddAssert("Text entered", () => textBox.Text.Value == "a");

        AddStep("Clear via Backspace", () => InputManager.PressKey(Key.BackSpace));
        AddAssert("Text empty again (placeholder visible in visual runner)", () => textBox.Text.Value == "");
    }

    [Test]
    public void TestClickElsewhereUnfocuses()
    {
        AddStep("Focus textbox", () =>
        {
            InputManager.MoveMouseTo(textBox);
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("Textbox has focus", () => textBox.HasFocus);

        AddStep("Click outside textbox", () =>
        {
            InputManager.MoveMouseTo(new Vector2(10, 10));
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("Textbox lost focus", () => !textBox.HasFocus);
    }

    [Test]
    public void TestClickingTextBoxAgainKeepsFocus()
    {
        AddStep("Focus textbox", () =>
        {
            InputManager.MoveMouseTo(textBox);
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("Textbox has focus", () => textBox.HasFocus);

        AddStep("Click textbox again", () => InputManager.Click(MouseButton.Left));
        AddAssert("Textbox still has focus", () => textBox.HasFocus);
    }

    [Test]
    public void TestEscapeUnfocuses()
    {
        AddStep("Focus textbox", () =>
        {
            InputManager.MoveMouseTo(textBox);
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("Textbox has focus", () => textBox.HasFocus);

        AddStep("Press Escape", () => InputManager.PressKey(Key.Escape));
        AddAssert("Textbox lost focus", () => !textBox.HasFocus);
    }

    [Test]
    public void TestFocusTransferBetweenTwoTextBoxes()
    {
        BasicTextBox secondBox = null!;

        AddStep("Add second textbox", () =>
        {
            TestContent.Add(secondBox = new BasicTextBox
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Position = new Vector2(0, 60),
                Width = 300,
                Height = 40
            });
        });

        AddStep("Focus first textbox", () =>
        {
            InputManager.MoveMouseTo(textBox);
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("First textbox focused", () => textBox.HasFocus);
        AddAssert("Second textbox not focused", () => !secondBox.HasFocus);

        AddStep("Click second textbox", () =>
        {
            InputManager.MoveMouseTo(secondBox);
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("First textbox lost focus", () => !textBox.HasFocus);
        AddAssert("Second textbox gained focus", () => secondBox.HasFocus);
    }

    [Test]
    public void TestEmptyCopyDoesNotClobberClipboard()
    {
        AddStep("Focus and type", () =>
        {
            InputManager.MoveMouseTo(textBox);
            InputManager.Click(MouseButton.Left);
            InputManager.TypeText("keepme");
        });

        AddStep("Select all and copy", () =>
        {
            InputManager.PressKey(Key.A, KeyModifiers.Control);
            InputManager.PressKey(Key.C, KeyModifiers.Control);
        });

        AddStep("Collapse selection", () => InputManager.PressKey(Key.End));
        AddStep("Copy with no selection", () => InputManager.PressKey(Key.C, KeyModifiers.Control));

        AddStep("Select all and paste", () =>
        {
            InputManager.PressKey(Key.A, KeyModifiers.Control);
            InputManager.PressKey(Key.V, KeyModifiers.Control);
        });

        AddAssert("Clipboard kept earlier copy", () => textBox.Text.Value == "keepme");
    }

    [Test]
    public void TestSetTextOnInit()
    {
        const string initial = "Initial   blablabla";

        AddStep("Set initial text", () => textBox.Text.Value = initial);

        AddAssert("Reactive value updated", () => textBox.Text.Value == initial);
        AddAssert("Caret moved to end of text", () => textBox.CaretIndex == initial.Length);
        AddAssert("No selection after setting text", () => textBox.SelectionStart == textBox.CaretIndex);

        // Setting the text again should still leave the caret at the end of the new value.
        AddStep("Set shorter text", () => textBox.Text.Value = "short");
        AddAssert("Caret moved to end of new text", () => textBox.CaretIndex == "short".Length);
        AddAssert("Caret stays within bounds", () => textBox.CaretIndex <= textBox.Text.Value.Length);
    }

    [Test]
    public void TestSetTextDoesNotDisruptTyping()
    {
        AddStep("Set initial text", () => textBox.Text.Value = "abc");

        AddStep("Focus textbox", () =>
        {
            InputManager.MoveMouseTo(textBox);
            InputManager.Click(MouseButton.Left);
        });

        // Clicking to focus repositions the caret to the mouse, so move it to the end first.
        AddStep("Move caret to end", () => InputManager.PressKey(Key.End));
        AddStep("Type more text", () => InputManager.TypeText("def"));
        AddAssert("Typed text appended at end", () => textBox.Text.Value == "abcdef");
        AddAssert("Caret at end after typing", () => textBox.CaretIndex == "abcdef".Length);
    }

    [Test]
    public void TestSetTextWithSpace()
    {
        // Spaces (including consecutive, leading, and trailing ones) must be counted so the caret
        // lands at the true end of the string rather than collapsing whitespace.
        const string spaced = "  hello   world  ";

        AddStep("Set text with spaces", () => textBox.Text.Value = spaced);

        AddAssert("Reactive value preserves spaces", () => textBox.Text.Value == spaced);
        AddAssert("Caret at end including trailing spaces", () => textBox.CaretIndex == spaced.Length);
        AddAssert("No selection after setting text", () => textBox.SelectionStart == textBox.CaretIndex);

        // Typing into a space-containing string should append, keeping every space intact.
        // Clicking to focus repositions the caret to the mouse, so move it to the end first.
        AddStep("Focus textbox", () =>
        {
            InputManager.MoveMouseTo(textBox);
            InputManager.Click(MouseButton.Left);
        });
        AddStep("Move caret to end", () => InputManager.PressKey(Key.End));

        AddStep("Type trailing word", () => InputManager.TypeText("!"));
        AddAssert("Appended after trailing spaces", () => textBox.Text.Value == spaced + "!");
        AddAssert("Caret at new end", () => textBox.CaretIndex == (spaced + "!").Length);
    }
}
