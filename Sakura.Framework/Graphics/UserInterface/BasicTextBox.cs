// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Graphics.UserInterface;

public partial class BasicTextBox : Container
{
    private readonly Box background;
    private readonly Container textContainer;
    private readonly Box selectionBox;
    private readonly SpriteText spriteText;
    private readonly SpriteText placeholderText;
    private readonly SpriteText imeText;
    private readonly Box caret;

    private int caretIndex;
    private int selectionStart;

    [Resolved]
    private IWindow window { get; set; } = null!;

    public override bool AcceptsFocus => true;

    public Reactive<string> Text { get; } = new Reactive<string>("");

    /// <summary>
    /// Fired when the user commits the text (presses Enter).
    /// </summary>
    public event Action<string>? OnCommit;

    /// <summary>
    /// Whether committing (Enter) also releases keyboard focus. Defaults to true.
    /// </summary>
    public bool ReleaseFocusOnCommit { get; set; } = true;

    /// <summary>
    /// Maximum number of characters this text box accepts. Null for unlimited.
    /// </summary>
    public int? LengthLimit { get; set; }

    /// <summary>
    /// Dimmed hint text shown while the text box is empty.
    /// </summary>
    public string PlaceholderText
    {
        get => placeholderText.Text;
        set
        {
            placeholderText.Text = value ?? "";
            updatePlaceholderVisibility();
        }
    }

    private Color backgroundColour = Color.Green;

    /// <summary>
    /// The background colour while unfocused.
    /// </summary>
    public Color BackgroundColour
    {
        get => backgroundColour;
        set
        {
            backgroundColour = value;
            if (!HasFocus)
                background.Color = value;
        }
    }

    /// <summary>
    /// The background colour while focused. Defaults to a lightened <see cref="BackgroundColour"/>.
    /// </summary>
    public Color? BackgroundFocusedColour { get; set; }

    private Color effectiveFocusedColour => BackgroundFocusedColour ?? backgroundColour.Lighten(0.3f);

    public BasicTextBox()
    {
        Size = new Vector2(200, 30);
        Masking = true;

        Children = new Drawable[]
        {
            background = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(1),
                Color = Color.Green
            },
            textContainer = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Children = new Drawable[]
                {
                    selectionBox = new Box
                    {
                        RelativeSizeAxes = Axes.Y,
                        Height = 0.8f,
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Color = Color.Blue,
                        Alpha = 0f
                    },
                    placeholderText = new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = "",
                        Color = Color.White,
                        Alpha = 0.4f,
                        Margin = new MarginPadding
                        {
                            Left = 5,
                            Right = 5
                        },
                        Font = FontUsage.Default.With(size: 16)
                    },
                    spriteText = new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = "",
                        Margin = new MarginPadding
                        {
                            Left = 5,
                            Right = 5
                        },
                        Font = FontUsage.Default.With(size: 16)
                    },
                    imeText = new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = "",
                        Color = Color.Yellow,
                        Margin = new MarginPadding
                        {
                            Left = 5,
                            Right = 5
                        },
                        Font = FontUsage.Default.With(size: 16)
                    },
                    caret = new Box
                    {
                        Width = 2,
                        RelativeSizeAxes = Axes.Y,
                        Height = 0.8f,
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Color = Color.White,
                        Alpha = 0
                    }
                }
            }
        };

        Text.ValueChanged += e =>
        {
            string newText = e.NewValue ?? "";
            spriteText.Text = newText;
            caretIndex = Math.Clamp(caretIndex, 0, newText.Length);
            selectionStart = Math.Clamp(selectionStart, 0, newText.Length);
            updatePlaceholderVisibility();
            Invalidate(InvalidationFlags.DrawInfo);
        };
    }

    private void updatePlaceholderVisibility()
    {
        placeholderText.Alpha = string.IsNullOrEmpty(Text.Value) ? 0.4f : 0f;
    }

    private void resetCaretBlink()
    {
        caret.ClearTransforms();
        caret.Alpha = 1;
        caret.FadeTo(0, 750)
            .Then()
            .FadeTo(1, 750).Loop();
    }

    /// <summary>
    /// To be called whenever the caret moves or text changes: shows the caret solid
    /// (restarting the blink cycle, as every standard text field does) and re-layouts.
    /// </summary>
    private void caretMoved()
    {
        if (HasFocus)
            resetCaretBlink();

        Invalidate(InvalidationFlags.DrawInfo);
    }

    #region Word Boundaries

    private static bool isWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private int findPreviousWordBoundary(int from)
    {
        string text = Text.Value;
        int i = Math.Clamp(from, 0, text.Length);

        // Skip separators, then the word itself.
        while (i > 0 && !isWordChar(text[i - 1])) i--;
        while (i > 0 && isWordChar(text[i - 1])) i--;
        return i;
    }

    private int findNextWordBoundary(int from)
    {
        string text = Text.Value;
        int i = Math.Clamp(from, 0, text.Length);

        while (i < text.Length && !isWordChar(text[i])) i++;
        while (i < text.Length && isWordChar(text[i])) i++;
        return i;
    }

    #endregion

    #region Focus and Input State

    public override void OnFocus(FocusEvent e)
    {
        base.OnFocus(e);
        resetCaretBlink();
        background.FadeToColour(effectiveFocusedColour, 150, Transforms.Easing.OutQuint);
        window?.StartTextInput();
        Invalidate(InvalidationFlags.DrawInfo);
    }

    public override void OnFocusLost(FocusLostEvent e)
    {
        base.OnFocusLost(e);

        caret.ClearTransforms();
        caret.Hide();

        background.FadeToColour(backgroundColour, 150, Transforms.Easing.OutQuint);

        selectionBox.Hide();
        imeText.Text = "";
        selectionStart = caretIndex;
        textContainer.X = 0;
        window.StopTextInput();
    }

    #endregion

    #region Mouse Interactions (Drag & Select)

    private int getIndexFromMouseX(float screenX)
    {
        float localX = screenX - DrawRectangle.X;

        float textSpaceX = localX - textContainer.X;

        int closestIndex = 0;
        float minDistance = float.MaxValue;

        for (int i = 0; i <= Text.Value.Length; i++)
        {
            float charX = spriteText.Margin.Left + spriteText.GetCharacterPosition(i).X;
            float distance = Math.Abs(textSpaceX - charX);

            if (distance < minDistance)
            {
                minDistance = distance;
                closestIndex = i;
            }
        }

        return closestIndex;
    }

    // Mouse events don't carry keyboard modifiers, so shift state is tracked from
    // key events to support Shift+Click selection extension.
    private bool shiftHeld;

    public override bool OnMouseDown(MouseButtonEvent e)
    {
        base.OnMouseDown(e);

        if (e.Button == MouseButton.Left)
        {
            if (e.Clicks == 1)
            {
                caretIndex = getIndexFromMouseX(e.ScreenSpaceMousePosition.X);

                // Shift+Click extends the selection from the existing anchor.
                if (!shiftHeld)
                    selectionStart = caretIndex;

                caretMoved();
            }
            return true;
        }
        return false;
    }

    public override bool OnDragStart(MouseButtonEvent e) => true;

    public override bool OnDrag(MouseEvent e)
    {
        caretIndex = getIndexFromMouseX(e.ScreenSpaceMousePosition.X);
        caretMoved();
        return true;
    }

    #endregion

    #region Keyboard Interactions

    private void deleteSelection()
    {
        if (selectionStart == caretIndex) return;

        int start = Math.Min(selectionStart, caretIndex);
        int length = Math.Abs(selectionStart - caretIndex);

        caretIndex = start;
        selectionStart = start;
        Text.Value = Text.Value.Remove(start, length);
    }

    /// <summary>
    /// Inserts text at the caret (replacing any selection), respecting <see cref="LengthLimit"/>.
    /// </summary>
    private void insertTextAtCaret(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        if (selectionStart != caretIndex)
            deleteSelection();

        if (LengthLimit is int limit)
        {
            int available = limit - Text.Value.Length;
            if (available <= 0)
                return;

            if (text.Length > available)
                text = text.Substring(0, available);
        }

        int insertIndex = caretIndex;
        caretIndex += text.Length;
        selectionStart = caretIndex;
        Text.Value = Text.Value.Insert(insertIndex, text);
        caretMoved();
    }

    public override bool OnKeyDown(KeyEvent e)
    {
        // Track shift for Shift+Click selection (mouse events carry no modifiers).
        if (e.Key == Key.ShiftLeft || e.Key == Key.ShiftRight)
            shiftHeld = true;

        if (!HasFocus) return false;

        if (e.Key == Key.Escape)
        {
            GetContainingFocusManager()?.ChangeFocus(null);
            return true;
        }

        if (e.Key == Key.Enter || e.Key == Key.KeypadEnter)
        {
            OnCommit?.Invoke(Text.Value);

            if (ReleaseFocusOnCommit)
                GetContainingFocusManager()?.ChangeFocus(null);

            return true;
        }

        bool controlPressed = (e.Modifiers & KeyModifiers.Control) > 0;
        bool shiftPressed = (e.Modifiers & KeyModifiers.Shift) > 0;
        bool hasSelection = selectionStart != caretIndex;

        if (controlPressed)
        {
            if (e.Key == Key.A)
            {
                selectionStart = 0;
                caretIndex = Text.Value.Length;
                caretMoved();
                return true;
            }

            if (e.Key == Key.C)
            {
                // Copy applies to the selection only (standard behaviour: an empty
                // selection must not clobber the clipboard).
                if (hasSelection)
                {
                    int start = Math.Min(selectionStart, caretIndex);
                    int length = Math.Abs(selectionStart - caretIndex);
                    window.SetClipboardText(Text.Value.Substring(start, length));
                }
                return true;
            }

            if (e.Key == Key.X)
            {
                if (hasSelection)
                {
                    int start = Math.Min(selectionStart, caretIndex);
                    int length = Math.Abs(selectionStart - caretIndex);
                    window.SetClipboardText(Text.Value.Substring(start, length));
                    deleteSelection();
                    caretMoved();
                }
                return true;
            }

            if (e.Key == Key.V)
            {
                string clipboardText = window.GetClipboardText() ?? "";
                if (!string.IsNullOrEmpty(clipboardText))
                    insertTextAtCaret(clipboardText);
                return true;
            }

            // Ctrl+Backspace: delete to the previous word boundary.
            if (e.Key == Key.BackSpace)
            {
                if (hasSelection)
                {
                    deleteSelection();
                }
                else if (caretIndex > 0)
                {
                    int boundary = findPreviousWordBoundary(caretIndex);
                    Text.Value = Text.Value.Remove(boundary, caretIndex - boundary);
                    caretIndex = boundary;
                    selectionStart = boundary;
                }

                caretMoved();
                return true;
            }

            // Ctrl+Delete: delete to the next word boundary.
            if (e.Key == Key.Delete)
            {
                if (hasSelection)
                {
                    deleteSelection();
                }
                else if (caretIndex < Text.Value.Length)
                {
                    int boundary = findNextWordBoundary(caretIndex);
                    Text.Value = Text.Value.Remove(caretIndex, boundary - caretIndex);
                }

                caretMoved();
                return true;
            }
        }

        if (e.Key == Key.Left)
        {
            if (controlPressed)
            {
                // Ctrl+Left: jump to the previous word boundary.
                caretIndex = findPreviousWordBoundary(caretIndex);
                if (!shiftPressed)
                    selectionStart = caretIndex;
            }
            else if (shiftPressed)
            {
                caretIndex = Math.Max(0, caretIndex - 1);
            }
            else
            {
                if (hasSelection)
                    caretIndex = Math.Min(selectionStart, caretIndex);
                else
                    caretIndex = Math.Max(0, caretIndex - 1);
                selectionStart = caretIndex;
            }

            caretMoved();
            return true;
        }

        if (e.Key == Key.Right)
        {
            if (controlPressed)
            {
                // Ctrl+Right: jump to the next word boundary.
                caretIndex = findNextWordBoundary(caretIndex);
                if (!shiftPressed)
                    selectionStart = caretIndex;
            }
            else if (shiftPressed)
            {
                caretIndex = Math.Min(Text.Value.Length, caretIndex + 1);
            }
            else
            {
                if (hasSelection)
                    caretIndex = Math.Max(selectionStart, caretIndex);
                else
                    caretIndex = Math.Min(Text.Value.Length, caretIndex + 1);
                selectionStart = caretIndex;
            }

            caretMoved();
            return true;
        }

        if (e.Key == Key.BackSpace)
        {
            if (hasSelection)
            {
                deleteSelection();
                caretMoved();
                return true;
            }
            if (caretIndex > 0)
            {
                caretIndex--;
                selectionStart = caretIndex;
                Text.Value = Text.Value.Remove(caretIndex, 1);
                caretMoved();
                return true;
            }
        }

        if (e.Key == Key.Delete)
        {
            if (hasSelection)
            {
                deleteSelection();
                caretMoved();
                return true;
            }
            if (caretIndex < Text.Value.Length)
            {
                Text.Value = Text.Value.Remove(caretIndex, 1);
                caretMoved();
                return true;
            }
        }

        if (e.Key == Key.Home)
        {
            caretIndex = 0;
            if (!shiftPressed)
                selectionStart = caretIndex;

            caretMoved();
            return true;
        }

        if (e.Key == Key.End)
        {
            caretIndex = Text.Value.Length;
            if (!shiftPressed)
                selectionStart = caretIndex;

            caretMoved();
            return true;
        }

        return base.OnKeyDown(e);
    }

    public override bool OnKeyUp(KeyEvent e)
    {
        if (e.Key == Key.ShiftLeft || e.Key == Key.ShiftRight)
            shiftHeld = false;

        return base.OnKeyUp(e);
    }

    public override bool OnDoubleClick(MouseButtonEvent e)
    {
        if (e.Button == MouseButton.Left)
        {
            // Double-click selects the word under the cursor (triple-click selects all).
            string text = Text.Value;
            int index = Math.Clamp(getIndexFromMouseX(e.ScreenSpaceMousePosition.X), 0, text.Length);

            int start = index;
            int end = index;

            if (text.Length > 0)
            {
                // Probe the character at (or just before) the click for word membership.
                int probe = Math.Min(index, text.Length - 1);
                bool word = isWordChar(text[probe]);

                while (start > 0 && isWordChar(text[start - 1]) == word) start--;
                while (end < text.Length && isWordChar(text[end]) == word) end++;
            }

            selectionStart = start;
            caretIndex = end;
            caretMoved();
            return true;
        }
        return base.OnDoubleClick(e);
    }

    public override bool OnTripleClick(MouseButtonEvent e)
    {
        if (e.Button == MouseButton.Left)
        {
            selectionStart = 0;
            caretIndex = Text.Value.Length;
            caretMoved();
            return true;
        }
        return base.OnTripleClick(e);
    }

    public override bool OnTextInput(TextInputEvent e)
    {
        if (!HasFocus) return false;

        imeText.Text = "";
        insertTextAtCaret(e.Text);

        return true;
    }

    public override bool OnTextEditing(TextEditingEvent e)
    {
        if (!HasFocus)
            return false;

        imeText.Text = e.Text;
        Invalidate(InvalidationFlags.DrawInfo);

        return true;
    }

    #endregion

    #region Layout and Graphics Updates

    protected internal override void UpdateTransforms()
    {
        base.UpdateTransforms();

        if (HasFocus)
        {
            Vector2 charPos = spriteText.GetCharacterPosition(caretIndex);
            float caretTargetX = spriteText.Margin.Left + charPos.X;
            caret.X = caretTargetX;

            imeText.Margin = new MarginPadding { Left = caretTargetX, Right = 5 };

            if (selectionStart != caretIndex)
            {
                Vector2 startPos = spriteText.GetCharacterPosition(selectionStart);
                float startTargetX = spriteText.Margin.Left + startPos.X;

                selectionBox.X = Math.Min(startTargetX, caretTargetX);
                selectionBox.Width = Math.Abs(caretTargetX - startTargetX);
                selectionBox.Alpha = 0.5f;
            }
            else
            {
                selectionBox.Alpha = 0f;
            }

            float padding = 10f;
            float absoluteCaretX = textContainer.X + caretTargetX;

            // push left if typing past the right edge
            if (absoluteCaretX > DrawSize.X - padding)
            {
                textContainer.X -= (absoluteCaretX - (DrawSize.X - padding));
            }
            // pull right if arrowing past the left edge
            else if (absoluteCaretX < padding)
            {
                textContainer.X += (padding - absoluteCaretX);
            }

            // prevent over-scrolling when deleting text (snap back to 0 if space allows)
            float totalTextWidth = spriteText.GetCharacterPosition(Text.Value.Length).X + spriteText.Margin.Left + spriteText.Margin.Right;
            if (totalTextWidth <= DrawSize.X || textContainer.X > 0)
            {
                textContainer.X = 0;
            }
            else if (textContainer.X + totalTextWidth < DrawSize.X)
            {
                textContainer.X = DrawSize.X - totalTextWidth;
            }

            if (window != null)
            {
                float normalizedX = DrawSize.X > 0 ? (textContainer.X + caretTargetX) / DrawSize.X : 0;
                var screenPos = Vector2.Transform(new Vector2(normalizedX, 0), ModelMatrix);
                window.SetTextInputRect(new RectangleF(screenPos.X, screenPos.Y, 0, DrawSize.Y));
            }
        }
    }

    #endregion
}
