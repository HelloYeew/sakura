// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
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
    private readonly SpriteText imeText;
    private readonly Box caret;

    private int caretIndex;
    private int selectionStart;

    [Resolved]
    private IWindow window { get; set; } = null!;

    public override bool AcceptsFocus => true;

    public Reactive<string> Text { get; } = new Reactive<string>("");

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
            Invalidate(InvalidationFlags.DrawInfo);
        };
    }

    private void resetCaretBlink()
    {
        caret.ClearTransforms();
        caret.Alpha = 1;
        caret.FadeTo(0, 750)
            .Then()
            .FadeTo(1, 750).Loop();
    }

    #region Focus and Input State

    public override void OnFocus(FocusEvent e)
    {
        base.OnFocus(e);
        resetCaretBlink();
        window?.StartTextInput();
        Invalidate(InvalidationFlags.DrawInfo);
    }

    public override void OnFocusLost(FocusLostEvent e)
    {
        base.OnFocusLost(e);

        caret.ClearTransforms();
        caret.Hide();

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

    public override bool OnMouseDown(MouseButtonEvent e)
    {
        base.OnMouseDown(e);

        if (e.Button == MouseButton.Left)
        {
            if (e.Clicks == 1)
            {
                caretIndex = getIndexFromMouseX(e.ScreenSpaceMousePosition.X);
                selectionStart = caretIndex;
                Invalidate(InvalidationFlags.DrawInfo);
            }
            return true;
        }
        return false;
    }

    public override bool OnDragStart(MouseButtonEvent e) => true;

    public override bool OnDrag(MouseEvent e)
    {
        caretIndex = getIndexFromMouseX(e.ScreenSpaceMousePosition.X);
        Invalidate(InvalidationFlags.DrawInfo);
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

    public override bool OnKeyDown(KeyEvent e)
    {
        if (!HasFocus) return false;

        if (e.Key == Key.Escape)
        {
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
                Invalidate(InvalidationFlags.DrawInfo);
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
                else if (!string.IsNullOrEmpty(Text.Value))
                {
                    window.SetClipboardText(Text.Value);
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
                }
                return true;
            }

            if (e.Key == Key.V)
            {
                string clipboardText = window.GetClipboardText() ?? "";
                if (!string.IsNullOrEmpty(clipboardText))
                {
                    if (hasSelection)
                        deleteSelection();

                    int insertIndex = caretIndex;
                    caretIndex += clipboardText.Length;
                    selectionStart = caretIndex;
                    Text.Value = Text.Value.Insert(insertIndex, clipboardText);
                }
                return true;
            }
        }

        if (e.Key == Key.Left)
        {
            if (shiftPressed)
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

            Invalidate(InvalidationFlags.DrawInfo);
            return true;
        }

        if (e.Key == Key.Right)
        {
            if (shiftPressed)
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

            Invalidate(InvalidationFlags.DrawInfo);
            return true;
        }

        if (e.Key == Key.BackSpace)
        {
            if (hasSelection)
            {
                deleteSelection();
                return true;
            }
            if (caretIndex > 0)
            {
                caretIndex--;
                selectionStart = caretIndex;
                Text.Value = Text.Value.Remove(caretIndex, 1);
                return true;
            }
        }

        if (e.Key == Key.Delete)
        {
            if (hasSelection)
            {
                deleteSelection();
                return true;
            }
            if (caretIndex < Text.Value.Length)
            {
                Text.Value = Text.Value.Remove(caretIndex, 1);
                return true;
            }
        }

        if (e.Key == Key.Home)
        {
            caretIndex = 0;
            if (!shiftPressed)
                selectionStart = caretIndex;

            Invalidate(InvalidationFlags.DrawInfo);
            return true;
        }

        if (e.Key == Key.End)
        {
            caretIndex = Text.Value.Length;
            if (!shiftPressed)
                selectionStart = caretIndex;

            Invalidate(InvalidationFlags.DrawInfo);
            return true;
        }

        return base.OnKeyDown(e);
    }

    public override bool OnDoubleClick(MouseButtonEvent e)
    {
        if (e.Button == MouseButton.Left)
        {
            // select all text on double-click
            selectionStart = 0;
            caretIndex = Text.Value.Length;
            Invalidate(InvalidationFlags.DrawInfo);
            return true;
        }
        return base.OnDoubleClick(e);
    }

    public override bool OnTextInput(TextInputEvent e)
    {
        if (!HasFocus) return false;

        imeText.Text = "";
        if (selectionStart != caretIndex)
            deleteSelection();

        int insertIndex = caretIndex;
        caretIndex += e.Text.Length;
        selectionStart = caretIndex;
        Text.Value = Text.Value.Insert(insertIndex, e.Text);

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
                var screenPos = Vector4.Transform(new Vector4(normalizedX, 0, 0, 1), ModelMatrix);
                window.SetTextInputRect(new RectangleF(screenPos.X, screenPos.Y, 0, DrawSize.Y));
            }
        }
    }

    #endregion
}
