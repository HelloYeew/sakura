// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Cursor;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Testing.Input;

public class ManualInputManager : Container
{
    /// <summary>
    /// If true, hardware inputs from the host/parent will be passed to children.
    /// If false, hardware inputs are ignored, and only manual synthetic inputs are processed.
    /// </summary>
    public bool UseParentInput { get; set; } = true;

    private readonly MouseState currentMouseState = new MouseState();
    private CursorContainer cursorContainer;

    public ManualInputManager()
    {
        RelativeSizeAxes = Axes.Both;
        Size = new Vector2(1);
        Add(cursorContainer = new CursorContainer());
    }

    public override bool OnMouseMove(MouseEvent e)
    {
        cursorContainer.OnMouseMove(e);
        if (!UseParentInput)
            return true;

        currentMouseState.Position = e.MouseState.Position;
        return base.OnMouseMove(e);
    }

    public override bool OnMouseDown(MouseButtonEvent e)
    {
        if (!UseParentInput)
            return true;

        currentMouseState.SetPressed(e.Button, true);
        return base.OnMouseDown(e);
    }

    public override bool OnMouseUp(MouseButtonEvent e)
    {
        if (!UseParentInput)
            return true;

        currentMouseState.SetPressed(e.Button, false);
        return base.OnMouseUp(e);
    }

    public override bool OnKeyDown(KeyEvent e)
    {
        if (!UseParentInput)
            return true;
        return base.OnKeyDown(e);
    }

    public override bool OnKeyUp(KeyEvent e)
    {
        if (!UseParentInput)
            return true;
        return base.OnKeyUp(e);
    }

    public override bool OnScroll(ScrollEvent e)
    {
        if (!UseParentInput)
            return true;
        return base.OnScroll(e);
    }

    /// <summary>
    /// Moves the synthetic mouse to the specified screen-space position.
    /// </summary>
    /// <param name="position">The target position in screen coordinates (relative to the top-left of the window).</param>
    public void MoveMouseTo(Vector2 position)
    {
        Vector2 delta = position - currentMouseState.Position;
        currentMouseState.Position = position;
        var syntheticEvent = new MouseEvent(currentMouseState, delta);
        cursorContainer.OnMouseMove(syntheticEvent);
        base.OnMouseMove(syntheticEvent);
    }

    /// <summary>
    /// Moves the synthetic mouse to the center of the specified <see cref="Drawable"/> in screen-space coordinates.
    /// </summary>
    /// <param name="target">The target drawable to move the mouse to.</param>
    public void MoveMouseTo(Drawable target)
    {
        var rect = target.DrawRectangle;
        var center = new Vector2(
            rect.X + rect.Width / 2f,
            rect.Y + rect.Height / 2f
        );
        MoveMouseTo(center);
    }

    /// <summary>
    /// Synthesizes a double click at the current mouse position.
    /// </summary>
    public void DoubleClick(MouseButton button)
    {
        // Send the event with '2' for the click count
        currentMouseState.SetPressed(button, true);
        base.OnMouseDown(new MouseButtonEvent(currentMouseState, button, 2));

        currentMouseState.SetPressed(button, false);
        base.OnMouseUp(new MouseButtonEvent(currentMouseState, button, 2));
    }

    /// <summary>
    /// Synthesizes a scroll-wheel movement.
    /// </summary>
    /// <param name="delta">The scroll amount (e.g., new Vector2(0, 1) to scroll up).</param>
    public void ScrollBy(Vector2 delta)
    {
        base.OnScroll(new ScrollEvent(currentMouseState, delta));
    }

    /// <summary>
    /// Synthesizes a complete drag-and-drop motion from one position to another.
    /// </summary>
    public void Drag(Vector2 startPosition, Vector2 endPosition, MouseButton button = MouseButton.Left)
    {
        MoveMouseTo(startPosition);
        PressButton(button);
        MoveMouseTo(endPosition);
        ReleaseButton(button);
    }

    /// <summary>
    /// Synthesizes a complete drag-and-drop motion from one Drawable to another.
    /// </summary>
    public void Drag(Drawable startTarget, Drawable endTarget, MouseButton button = MouseButton.Left)
    {
        MoveMouseTo(startTarget);
        PressButton(button);
        MoveMouseTo(endTarget);
        ReleaseButton(button);
    }

    /// <summary>
    /// Synthesizes a mouse button press at the current mouse position.
    /// </summary>
    /// <param name="button">The mouse button to press.</param>
    public void PressButton(MouseButton button)
    {
        currentMouseState.SetPressed(button, true);
        base.OnMouseDown(new MouseButtonEvent(currentMouseState, button, 1));
    }

    /// <summary>
    /// Synthesizes a mouse button release at the current mouse position.
    /// </summary>
    /// <param name="button">The mouse button to release.</param>
    public void ReleaseButton(MouseButton button)
    {
        currentMouseState.SetPressed(button, false);
        base.OnMouseUp(new MouseButtonEvent(currentMouseState, button, 1));
    }

    /// <summary>
    /// Synthesizes a complete click (press and release) at the current mouse position.
    /// </summary>
    /// <param name="button">The mouse button to click.</param>
    public void Click(MouseButton button)
    {
        PressButton(button);
        ReleaseButton(button);
    }

    /// <summary>
    /// Synthesizes a key press at the current focus, can include modifiers.
    /// </summary>
    /// <param name="key">The key to press.</param>
    /// <param name="modifiers">Optional key modifiers (e.g., Shift, Control).</param>
    public void PressKey(Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        base.OnKeyDown(new KeyEvent(key, modifiers, false));
    }

    /// <summary>
    /// Synthesizes a key release at the current focus, can include modifiers.
    /// </summary>
    /// <param name="key">The key to release.</param>
    /// <param name="modifiers">Optional key modifiers (e.g., Shift, Control).</param>
    public void ReleaseKey(Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        base.OnKeyUp(new KeyEvent(key, modifiers, false));
    }
}
