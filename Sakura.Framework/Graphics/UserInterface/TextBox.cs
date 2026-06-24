// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Graphics.UserInterface;

/// <summary>
/// Abstract base for textbox
/// </summary>
public abstract partial class TextBox : Container
{
    /// <summary>
    /// Scrolling container that holds text, selection, caret, and IME overlay.
    /// </summary>
    protected readonly Container TextContainer;

    /// <summary>
    /// The committed text sprite.
    /// </summary>
    protected readonly SpriteText SpriteText;

    /// <summary>
    /// Dimmed placeholder sprite shown while <see cref="Text"/> is empty.
    /// </summary>
    protected readonly SpriteText PlaceholderSprite;

    /// <summary>
    /// IME composition overlay sprite.
    /// </summary>
    protected readonly SpriteText ImeText;

    /// <summary>
    /// Caret drawable returned by <see cref="CreateCaret"/>.
    /// </summary>
    protected readonly Drawable Caret;

    /// <summary>
    /// Selection highlight returned by <see cref="CreateSelectionBox"/>.
    /// </summary>
    protected readonly Drawable SelectionBox;

    /// <summary>
    /// Background drawable returned by <see cref="CreateBackground"/>. May be null if the subclass returns null.
    /// </summary>
    protected readonly Drawable? Background;

    private int caretIndex;
    private int selectionStart;
    private bool shiftHeld;

    /// <summary>
    /// The current caret index within <see cref="Text"/>.
    /// </summary>
    public int CaretIndex => caretIndex;

    /// <summary>
    /// The current selection anchor index within <see cref="Text"/>. Equal to <see cref="CaretIndex"/>
    /// when there is no selection.
    /// </summary>
    public int SelectionStart => selectionStart;

    /// <summary>
    /// Set internally while the text value is being changed programmatically (i.e. not via a user
    /// edit such as typing, pasting, or deleting). Used to decide whether the caret should jump to
    /// the end of the new text.
    /// </summary>
    private bool isEditingText;

    /// <summary>
    /// True while an IME composition is in progress (SDL_TEXTEDITING received with non-empty text).
    /// Enter/Escape are suppressed during composition so they don't accidentally commit or close the text box.
    /// </summary>
    private bool isComposing;

    [Resolved]
    private IWindow window { get; set; } = null!;

    public override bool AcceptsFocus => true;

    /// <summary>
    /// The current text value.
    /// </summary>
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
    /// Maximum number of characters. Null for unlimited.
    /// </summary>
    public int? LengthLimit { get; set; }

    private double caretBlinkDuration = 750;

    /// <summary>
    /// Duration in milliseconds of a single caret fade (i.e. half of a full blink cycle: the time
    /// to fade from visible to hidden, and again from hidden to visible). Defaults to 750ms.
    /// Set to 0 to disable blinking and keep the caret permanently visible while focused.
    /// </summary>
    public double CaretBlinkDuration
    {
        get => caretBlinkDuration;
        set
        {
            caretBlinkDuration = Math.Max(0, value);

            // Apply the new timing immediately if the caret is currently active.
            if (HasFocus)
                resetCaretBlink();
        }
    }

    /// <summary>
    /// Dimmed hint text shown while the box is empty.
    /// </summary>
    public string PlaceholderText
    {
        get => PlaceholderSprite.Text;
        set
        {
            PlaceholderSprite.Text = value ?? "";
            UpdatePlaceholderVisibility();
        }
    }

    protected TextBox()
    {
        Masking = true;

        Caret = CreateCaret();
        SelectionBox = CreateSelectionBox();
        Background = CreateBackground();

        var textContainer = TextContainer = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Children = new[]
            {
                SelectionBox,
                PlaceholderSprite = new SpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Text = "",
                    Alpha = 0.4f,
                    Margin = new MarginPadding { Left = 5, Right = 5 },
                    Font = FontUsage.Default.With(size: 16)
                },
                SpriteText = new SpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Text = "",
                    Margin = new MarginPadding { Left = 5, Right = 5 },
                    Font = FontUsage.Default.With(size: 16)
                },
                ImeText = new SpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Text = "",
                    Margin = new MarginPadding { Left = 5, Right = 5 },
                    Font = FontUsage.Default.With(size: 16)
                },
                Caret
            }
        };

        if (Background != null)
        {
            AddInternal(Background);
            AddInternal(textContainer);
        }
        else
        {
            AddInternal(textContainer);
        }

        Text.ValueChanged += e =>
        {
            string newText = e.NewValue ?? "";
            SpriteText.Text = newText;

            if (isEditingText)
            {
                // A user edit (typing, pasting, deleting) already positioned the caret itself,
                // so we only clamp to keep it within the new bounds.
                caretIndex = Math.Clamp(caretIndex, 0, newText.Length);
                selectionStart = Math.Clamp(selectionStart, 0, newText.Length);
            }
            else
            {
                // The value was assigned programmatically (e.g. setting initial text). Place the
                // caret at the end of the new text, matching standard text box behaviour.
                caretIndex = newText.Length;
                selectionStart = newText.Length;
            }

            UpdatePlaceholderVisibility();
            caretMoved();
        };
    }

    /// <summary>
    /// Create and return the background drawable, drawn behind all text content.
    /// Return null for no background. Called once in the constructor.
    /// </summary>
    protected virtual Drawable? CreateBackground() => null;

    /// <summary>
    /// Create and return the caret drawable.  Called once in the constructor.
    /// The base implementation returns a white 2px wide box that blinks.
    /// Override to provide a custom-styled caret.
    /// </summary>
    protected virtual Drawable CreateCaret() => new Box
    {
        Width = 2,
        RelativeSizeAxes = Axes.Y,
        Height = 0.8f,
        Anchor = Anchor.CentreLeft,
        Origin = Anchor.CentreLeft,
        Alpha = 0
    };

    /// <summary>
    /// Create and return the selection highlight drawable.  Called once in the constructor.
    /// The base implementation returns a semi-transparent blue box.
    /// Override to provide custom styling.
    /// </summary>
    protected virtual Drawable CreateSelectionBox() => new Box
    {
        RelativeSizeAxes = Axes.Y,
        Height = 0.8f,
        Anchor = Anchor.CentreLeft,
        Origin = Anchor.CentreLeft,
        Alpha = 0f
    };

    /// <summary>
    /// Called when the text box gains keyboard focus. Animate background here.
    /// </summary>
    protected virtual void OnFocusGained() { }

    /// <summary>
    /// Called when the text box loses keyboard focus. Restore background here.
    /// </summary>
    protected virtual void OnFocusLost() { }

    /// <summary>
    /// Updates placeholder visibility based on whether <see cref="Text"/> is empty.
    /// </summary>
    protected virtual void UpdatePlaceholderVisibility()
    {
        PlaceholderSprite.Alpha = string.IsNullOrEmpty(Text.Value) ? 0.4f : 0f;
    }

    private void resetCaretBlink()
    {
        Caret.ClearTransforms();
        Caret.Alpha = 1;

        if (caretBlinkDuration <= 0)
            return;

        Caret.FadeTo(0, caretBlinkDuration).Then().FadeTo(1, caretBlinkDuration).Loop();
    }

    private void caretMoved()
    {
        if (HasFocus)
            resetCaretBlink();

        Invalidate(InvalidationFlags.DrawInfo);
    }

    private static bool isWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private int findPreviousWordBoundary(int from)
    {
        string text = Text.Value;
        int i = Math.Clamp(from, 0, text.Length);
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

    public override void OnFocus(FocusEvent e)
    {
        base.OnFocus(e);
        resetCaretBlink();
        OnFocusGained();
        window?.StartTextInput();
        Invalidate(InvalidationFlags.DrawInfo);
    }

    public override void OnFocusLost(FocusLostEvent e)
    {
        base.OnFocusLost(e);

        Caret.ClearTransforms();
        Caret.Hide();

        OnFocusLost();

        isComposing = false;
        SelectionBox.Hide();
        ImeText.Text = "";
        selectionStart = caretIndex;
        TextContainer.X = 0;
        window?.StopTextInput();
    }

    private int getIndexFromMouseX(float screenX)
    {
        float localX = screenX - DrawRectangle.X;
        float textSpaceX = localX - TextContainer.X;

        int closestIndex = 0;
        float minDistance = float.MaxValue;

        for (int i = 0; i <= Text.Value.Length; i++)
        {
            float charX = SpriteText.Margin.Left + SpriteText.GetCharacterPosition(i).X;
            float distance = Math.Abs(textSpaceX - charX);

            if (distance < minDistance)
            {
                minDistance = distance;
                closestIndex = i;
            }
        }

        return closestIndex;
    }

    public override bool OnMouseDown(MouseButtonEvent e)
    {
        base.OnMouseDown(e);

        if (e.Button == MouseButton.Left)
        {
            if (e.Clicks == 1)
            {
                caretIndex = getIndexFromMouseX(e.ScreenSpaceMousePosition.X);
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

    /// <summary>
    /// Assigns <see cref="Text"/> as part of a user edit (typing, pasting, deleting). The caller is
    /// responsible for having already positioned <see cref="caretIndex"/>; this flag tells the
    /// <see cref="Text"/> change handler not to reset the caret to the end of the text.
    /// </summary>
    private void setTextFromUserEdit(string value)
    {
        isEditingText = true;

        try
        {
            Text.Value = value;
        }
        finally
        {
            isEditingText = false;
        }
    }

    private void deleteSelection()
    {
        if (selectionStart == caretIndex) return;

        int start = Math.Min(selectionStart, caretIndex);
        int length = Math.Abs(selectionStart - caretIndex);

        caretIndex = start;
        selectionStart = start;
        setTextFromUserEdit(Text.Value.Remove(start, length));
    }

    private void insertTextAtCaret(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        if (selectionStart != caretIndex)
            deleteSelection();

        if (LengthLimit is int limit)
        {
            int available = limit - Text.Value.Length;
            if (available <= 0) return;
            if (text.Length > available)
                text = text.Substring(0, available);
        }

        int insertIndex = caretIndex;
        caretIndex += text.Length;
        selectionStart = caretIndex;
        setTextFromUserEdit(Text.Value.Insert(insertIndex, text));
        caretMoved();
    }

    public override bool OnKeyDown(KeyEvent e)
    {
        if (e.Key == Key.ShiftLeft || e.Key == Key.ShiftRight)
            shiftHeld = true;

        if (!HasFocus) return false;

        if (e.Key == Key.Escape)
        {
            // During IME composition, Escape cancels the composition; SDL handles it and
            // fires an empty TextEditing event, so we just absorb the key here.
            if (isComposing) return true;

            GetContainingFocusManager()?.ChangeFocus(null);
            return true;
        }

        if (e.Key == Key.Enter || e.Key == Key.KeypadEnter)
        {
            // Enter while composing confirms the candidate inside the IME popup.
            // SDL will fire SDL_TEXTINPUT with the chosen text, so we must not also
            // treat this as a text-box commit — just absorb the key.
            if (isComposing) return true;

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

            if (e.Key == Key.BackSpace)
            {
                if (hasSelection)
                    deleteSelection();
                else if (caretIndex > 0)
                {
                    int boundary = findPreviousWordBoundary(caretIndex);
                    string updated = Text.Value.Remove(boundary, caretIndex - boundary);
                    caretIndex = boundary;
                    selectionStart = boundary;
                    setTextFromUserEdit(updated);
                }
                caretMoved();
                return true;
            }

            if (e.Key == Key.Delete)
            {
                if (hasSelection)
                    deleteSelection();
                else if (caretIndex < Text.Value.Length)
                {
                    int boundary = findNextWordBoundary(caretIndex);
                    setTextFromUserEdit(Text.Value.Remove(caretIndex, boundary - caretIndex));
                }
                caretMoved();
                return true;
            }
        }

        if (e.Key == Key.Left)
        {
            if (controlPressed)
            {
                caretIndex = findPreviousWordBoundary(caretIndex);
                if (!shiftPressed) selectionStart = caretIndex;
            }
            else if (shiftPressed)
            {
                caretIndex = Math.Max(0, caretIndex - 1);
            }
            else
            {
                caretIndex = hasSelection ? Math.Min(selectionStart, caretIndex) : Math.Max(0, caretIndex - 1);
                selectionStart = caretIndex;
            }
            caretMoved();
            return true;
        }

        if (e.Key == Key.Right)
        {
            if (controlPressed)
            {
                caretIndex = findNextWordBoundary(caretIndex);
                if (!shiftPressed) selectionStart = caretIndex;
            }
            else if (shiftPressed)
            {
                caretIndex = Math.Min(Text.Value.Length, caretIndex + 1);
            }
            else
            {
                caretIndex = hasSelection ? Math.Max(selectionStart, caretIndex) : Math.Min(Text.Value.Length, caretIndex + 1);
                selectionStart = caretIndex;
            }
            caretMoved();
            return true;
        }

        if (e.Key == Key.BackSpace)
        {
            if (hasSelection) { deleteSelection(); caretMoved(); return true; }
            if (caretIndex > 0)
            {
                caretIndex--;
                selectionStart = caretIndex;
                setTextFromUserEdit(Text.Value.Remove(caretIndex, 1));
                caretMoved();
                return true;
            }
        }

        if (e.Key == Key.Delete)
        {
            if (hasSelection) { deleteSelection(); caretMoved(); return true; }
            if (caretIndex < Text.Value.Length)
            {
                setTextFromUserEdit(Text.Value.Remove(caretIndex, 1));
                caretMoved();
                return true;
            }
        }

        if (e.Key == Key.Home)
        {
            caretIndex = 0;
            if (!shiftPressed) selectionStart = caretIndex;
            caretMoved();
            return true;
        }

        if (e.Key == Key.End)
        {
            caretIndex = Text.Value.Length;
            if (!shiftPressed) selectionStart = caretIndex;
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
            string text = Text.Value;
            int index = Math.Clamp(getIndexFromMouseX(e.ScreenSpaceMousePosition.X), 0, text.Length);
            int start = index, end = index;

            if (text.Length > 0)
            {
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

        // Composition is confirmed — clear the IME overlay and insert the final text.
        isComposing = false;
        ImeText.Text = "";
        insertTextAtCaret(e.Text);
        return true;
    }

    public override bool OnTextEditing(TextEditingEvent e)
    {
        if (!HasFocus) return false;

        // An empty editing event signals that the composition was cancelled.
        isComposing = !string.IsNullOrEmpty(e.Text);
        ImeText.Text = e.Text;
        Invalidate(InvalidationFlags.DrawInfo);
        return true;
    }

    protected internal override void UpdateTransforms()
    {
        base.UpdateTransforms();

        if (HasFocus)
        {
            Vector2 charPos = SpriteText.GetCharacterPosition(caretIndex);
            float caretTargetX = SpriteText.Margin.Left + charPos.X;
            Caret.X = caretTargetX;

            ImeText.Margin = new MarginPadding { Left = caretTargetX, Right = 5 };

            if (selectionStart != caretIndex)
            {
                Vector2 startPos = SpriteText.GetCharacterPosition(selectionStart);
                float startTargetX = SpriteText.Margin.Left + startPos.X;

                SelectionBox.X = Math.Min(startTargetX, caretTargetX);
                SelectionBox.Width = Math.Abs(caretTargetX - startTargetX);
                SelectionBox.Alpha = 0.5f;
            }
            else
            {
                SelectionBox.Alpha = 0f;
            }

            const float padding = 10f;
            float absoluteCaretX = TextContainer.X + caretTargetX;

            if (absoluteCaretX > DrawSize.X - padding)
                TextContainer.X -= absoluteCaretX - (DrawSize.X - padding);
            else if (absoluteCaretX < padding)
                TextContainer.X += padding - absoluteCaretX;

            float totalTextWidth = SpriteText.GetCharacterPosition(Text.Value.Length).X + SpriteText.Margin.Left + SpriteText.Margin.Right;
            if (totalTextWidth <= DrawSize.X || TextContainer.X > 0)
                TextContainer.X = 0;
            else if (TextContainer.X + totalTextWidth < DrawSize.X)
                TextContainer.X = DrawSize.X - totalTextWidth;

            if (window != null)
            {
                float normalizedX = DrawSize.X > 0 ? (TextContainer.X + caretTargetX) / DrawSize.X : 0;
                var screenPos = Vector2.Transform(new Vector2(normalizedX, 0), ModelMatrix);
                window.SetTextInputRect(new RectangleF(screenPos.X, screenPos.Y, 0, DrawSize.Y));
            }
        }
    }
}
