// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Testing.Input;

public partial class ManualInputManager : Container, IInputManagerProvider, IFocusManager
{
    /// <summary>
    /// If true, hardware inputs from the host/parent will be passed to children.
    /// If false, hardware inputs are ignored, and only manual synthetic inputs are processed.
    /// </summary>
    public bool UseParentInput { get; set; } = true;

    private readonly MouseState currentMouseState = new MouseState();

    /// <summary>
    /// Exposes a shared <see cref="InputState"/> for descendants that read it via
    /// <see cref="Drawable.GetContainingInputManager"/> (e.g. ScrollableContainer's hover refresh).
    /// Kept in sync with the synthetic cursor position so test input drives the same state path as
    /// real input. As the nearest provider, this is found before the app's manager.
    /// </summary>
    public InputManager InputManager { get; } = new InputManager();

    public Drawable? FocusedDrawable => InputManager.FocusedDrawable;
    public IReadOnlyList<Drawable> FocusStack => InputManager.FocusStack;
    public bool WasFocusClaimedByLastClick => InputManager.WasFocusClaimedByLastClick;
    public void BeginMouseDownFocusTracking() => InputManager.BeginMouseDownFocusTracking();
    public bool ChangeFocus(Drawable? potentialFocusTarget) => InputManager.ChangeFocus(potentialFocusTarget);
    public void TriggerFocusContention(Drawable? triggerSource) => InputManager.TriggerFocusContention(triggerSource);
    public bool MoveFocusToNextTabStop(bool reverse = false) => InputManager.MoveFocusToNextTabStop(reverse);

    private readonly TouchMouseEmulation touchEmulation;

    public ManualInputManager()
    {
        RelativeSizeAxes = Axes.Both;
        Size = new Vector2(1);
        touchEmulation = new TouchMouseEmulation(this, InputManager, dispatchMouseMove, dispatchMouseDown, dispatchMouseUp);
    }

    public override void Update()
    {
        base.Update();
        touchEmulation.Update();
    }

    public override bool HandleNonPositionalInput => UseParentInput && base.HandleNonPositionalInput;
    public override bool HandlePositionalInput => UseParentInput && base.HandlePositionalInput;


    public override bool OnMouseMove(MouseEvent e) => !UseParentInput || dispatchMouseMove(e);

    public override bool OnMouseDown(MouseButtonEvent e) => !UseParentInput || dispatchMouseDown(e);

    public override bool OnMouseUp(MouseButtonEvent e) => !UseParentInput || dispatchMouseUp(e);

    private bool dispatchMouseMove(MouseEvent e)
    {
        currentMouseState.Position = e.MouseState.Position;
        InputManager.HandleMouseMove(e.MouseState.Position, e.MouseState.FromTouch);
        rebuildQueues();
        return InputManager.DispatchMouseMove(e);
    }

    private bool dispatchMouseDown(MouseButtonEvent e)
    {
        currentMouseState.Position = e.ScreenSpaceMousePosition;
        currentMouseState.SetPressed(e.Button, true);
        InputManager.HandleMouseDown(e.Button, e.ScreenSpaceMousePosition, e.MouseState.FromTouch);
        rebuildQueues();
        BeginMouseDownFocusTracking();
        bool handled = InputManager.DispatchMouseDown(e);
        if (!WasFocusClaimedByLastClick)
            ChangeFocus(null);
        return handled;
    }

    private bool dispatchMouseUp(MouseButtonEvent e)
    {
        currentMouseState.SetPressed(e.Button, false);
        InputManager.HandleMouseUp(e.Button, e.ScreenSpaceMousePosition, e.MouseState.FromTouch);
        rebuildQueues();
        return InputManager.DispatchMouseUp(e);
    }

    // The parent's input reaches what's in here directly, so the parent stops hovering this when the pointer leaves it
    // or a finger lifts, and with no pointer over this, nothing in it is hovered either. This manager's own hover
    // changes (it hovers itself as its queue's root) are already taking care of that.
    public override bool OnHoverLost(MouseEvent e)
    {
        if (!InputManager.UpdatingHover)
            InputManager.ClearHover();

        return base.OnHoverLost(e);
    }

    // Real fingers reach a drawable in here that takes them; one nothing takes is the parent's to turn into
    // mouse input, which then comes in through the mouse handlers above.
    public override bool OnTouchDown(TouchEvent e)
    {
        if (!UseParentInput)
            return true;

        InputManager.HandleTouch(e.Source, e.ScreenSpacePosition, true);
        InputManager.BuildQueues(this, e.ScreenSpacePosition);
        return InputManager.DispatchTouchDown(e);
    }

    public override bool OnTouchMove(TouchEvent e)
    {
        if (!UseParentInput)
            return true;

        InputManager.HandleTouch(e.Source, e.ScreenSpacePosition, true);
        return InputManager.DispatchTouchMove(e);
    }

    public override bool OnTouchUp(TouchEvent e)
    {
        if (!UseParentInput)
            return true;

        InputManager.HandleTouch(e.Source, e.ScreenSpacePosition, false);
        return InputManager.DispatchTouchUp(e);
    }

    /// <summary>
    /// Synthesizes a finger going down at <paramref name="position"/> (screen space). As with a real finger,
    /// one nothing takes acts as the mouse.
    /// </summary>
    public void TouchDown(TouchSource source, Vector2 position)
    {
        var e = new TouchEvent(source, position, timestamp: TimeSource.CurrentTime);

        InputManager.HandleTouch(source, position, true);
        InputManager.BuildQueues(this, position);

        touchEmulation.TouchDown(e, InputManager.DispatchTouchDown(e));
    }

    /// <summary>
    /// Synthesizes a finger that is down moving to <paramref name="position"/> (screen space).
    /// </summary>
    public void MoveTouchTo(TouchSource source, Vector2 position)
    {
        var e = new TouchEvent(source, position, position - InputManager.CurrentState.GetTouchPosition(source), TimeSource.CurrentTime);

        InputManager.HandleTouch(source, position, true);

        if (!InputManager.DispatchTouchMove(e))
            touchEmulation.TouchMove(e);
    }

    /// <summary>
    /// Synthesizes a finger lifting where it is. <paramref name="cancelled"/> has the system take it instead.
    /// </summary>
    public void TouchUp(TouchSource source, bool cancelled = false)
    {
        var position = InputManager.CurrentState.GetTouchPosition(source);
        var e = new TouchEvent(source, position, timestamp: TimeSource.CurrentTime, cancelled: cancelled);

        InputManager.HandleTouch(source, position, false);

        if (!InputManager.DispatchTouchUp(e))
            touchEmulation.TouchUp(e);
    }

    /// <summary>
    /// Synthesizes a tap: a finger down and straight back up at <paramref name="position"/>.
    /// </summary>
    public void Tap(Vector2 position, TouchSource source = TouchSource.Touch1)
    {
        TouchDown(source, position);
        TouchUp(source);
    }

    /// <summary>
    /// Synthesizes a tap on the centre of <paramref name="target"/>.
    /// </summary>
    public void Tap(Drawable target, TouchSource source = TouchSource.Touch1)
    {
        var rect = target.DrawRectangle;
        Tap(new Vector2(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f), source);
    }

    public override bool OnKeyDown(KeyEvent e)
    {
        if (!UseParentInput)
            return true;

        if (!e.IsRepeat)
            InputManager.HandleKeyDown(e.Key);
        rebuildQueues();
        return dispatchKeyDownWithFocus(e);
    }

    public override bool OnKeyUp(KeyEvent e)
    {
        if (!UseParentInput)
            return true;

        InputManager.HandleKeyUp(e.Key);
        rebuildQueues();
        return InputManager.DispatchKeyUp(e);
    }

    public override bool OnScroll(ScrollEvent e)
    {
        if (!UseParentInput)
            return true;

        InputManager.HandleScroll(e.ScreenSpaceMousePosition);
        rebuildQueues();
        return InputManager.DispatchScroll(e);
    }

    /// <summary>
    /// Rebuilds the input queues for this manager's subtree at the current synthetic cursor position.
    /// </summary>
    private void rebuildQueues() => InputManager.BuildQueues(this, currentMouseState.Position);

    /// <summary>
    /// Delivers a key-down to the focused drawable first (mirroring App), then down the non-positional
    /// queue. Assumes the queues are already built.
    /// </summary>
    private bool dispatchKeyDownWithFocus(KeyEvent e) => InputManager.DispatchKeyDownWithFocus(e);

    /// <summary>
    /// Moves the synthetic mouse to the specified screen-space position.
    /// </summary>
    /// <param name="position">The target position in screen coordinates (relative to the top-left of the window).</param>
    public void MoveMouseTo(Vector2 position)
    {
        Vector2 delta = position - currentMouseState.Position;
        currentMouseState.Position = position;
        InputManager.HandleMouseMove(position);
        rebuildQueues();
        InputManager.DispatchMouseMove(new MouseEvent(currentMouseState, delta));
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
    /// Synthesizes a double click at the current mouse position: a single click followed by one the
    /// OS counts as the second, as real input delivers it.
    /// </summary>
    public void DoubleClick(MouseButton button) => MultiClick(button, 2);

    /// <summary>
    /// Synthesizes a fast burst of complete clicks at the current mouse position, counted 1, 2, 3...
    /// the way the OS counts presses inside its double-click time (e.g. spamming a button).
    /// </summary>
    /// <param name="button">The mouse button to click.</param>
    /// <param name="count">How many clicks the burst has.</param>
    public void MultiClick(MouseButton button, int count)
    {
        for (int clicks = 1; clicks <= count; clicks++)
        {
            pressButton(button, clicks);
            releaseButton(button, clicks);
        }
    }

    /// <summary>
    /// Synthesizes a scroll-wheel movement.
    /// </summary>
    /// <param name="delta">The scroll amount (e.g., new Vector2(0, 1) to scroll up).</param>
    public void ScrollBy(Vector2 delta)
    {
        InputManager.HandleScroll(currentMouseState.Position);
        rebuildQueues();
        InputManager.DispatchScroll(new ScrollEvent(currentMouseState, delta));
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
    public void PressButton(MouseButton button) => pressButton(button, 1);

    private void pressButton(MouseButton button, int clicks)
    {
        currentMouseState.SetPressed(button, true);
        InputManager.HandleMouseDown(button, currentMouseState.Position);
        rebuildQueues();

        // Begin focus tracking so we can detect whether a focusable claimed focus.
        BeginMouseDownFocusTracking();

        InputManager.DispatchMouseDown(new MouseButtonEvent(currentMouseState, button, clicks));

        // If nothing claimed focus during this click, clear focus — same logic as App.OnMouseDown.
        if (!WasFocusClaimedByLastClick)
            ChangeFocus(null);
    }

    /// <summary>
    /// Synthesizes a mouse button release at the current mouse position.
    /// </summary>
    /// <param name="button">The mouse button to release.</param>
    public void ReleaseButton(MouseButton button) => releaseButton(button, 1);

    private void releaseButton(MouseButton button, int clicks)
    {
        currentMouseState.SetPressed(button, false);
        InputManager.HandleMouseUp(button, currentMouseState.Position);
        rebuildQueues();
        InputManager.DispatchMouseUp(new MouseButtonEvent(currentMouseState, button, clicks));
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
        // Feed the synthetic press (and any modifier flags, as their left-side physical keys) into the
        // shared InputState so descendants reading it (e.g. KeyBindingContainer) see the press.
        foreach (var modifierKey in modifierKeysFor(modifiers))
            InputManager.HandleKeyDown(modifierKey);
        InputManager.HandleKeyDown(key);
        rebuildQueues();
        dispatchKeyDownWithFocus(new KeyEvent(key, modifiers, false));
    }

    /// <summary>
    /// Synthesizes the OS auto-repeating a key held down with <see cref="PressKey"/>.
    /// </summary>
    /// <param name="key">The held key.</param>
    /// <param name="modifiers">Optional key modifiers (e.g., Shift, Control).</param>
    public void RepeatKey(Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        rebuildQueues();
        dispatchKeyDownWithFocus(new KeyEvent(key, modifiers, true));
    }

    /// <summary>
    /// Synthesizes a key release at the current focus, can include modifiers.
    /// </summary>
    /// <param name="key">The key to release.</param>
    /// <param name="modifiers">Optional key modifiers (e.g., Shift, Control).</param>
    public void ReleaseKey(Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        InputManager.HandleKeyUp(key);
        rebuildQueues();
        InputManager.DispatchKeyUp(new KeyEvent(key, modifiers, false));
    }

    // Maps modifier flags to the left-side physical keys we register in the shared InputState.
    private static IEnumerable<Key> modifierKeysFor(KeyModifiers modifiers)
    {
        if ((modifiers & KeyModifiers.Shift) != 0)
            yield return Key.ShiftLeft;
        if ((modifiers & KeyModifiers.Control) != 0)
            yield return Key.ControlLeft;
        if ((modifiers & KeyModifiers.Alt) != 0)
            yield return Key.AltLeft;
    }

    /// <summary>
    /// Synthesizes a native text input commit at the current focus.
    /// </summary>
    /// <param name="text">The string content to commit.</param>
    public void TypeText(string text)
    {
        rebuildQueues();
        InputManager.DispatchTextInput(new TextInputEvent(text));
    }

    /// <summary>
    /// Synthesizes an active IME composition layout editing sequence.
    /// </summary>
    public void EditComposingText(string text, int start, int length)
    {
        rebuildQueues();
        InputManager.DispatchTextEditing(new TextEditingEvent(text, start, length));
    }

    private readonly Dictionary<int, GamepadState> gamepadStates = new Dictionary<int, GamepadState>();

    private GamepadState getOrCreateGamepadState(int deviceId)
    {
        if (!gamepadStates.TryGetValue(deviceId, out var state))
        {
            state = new GamepadState { DeviceId = deviceId };
            gamepadStates[deviceId] = state;
        }

        return state;
    }

    /// <summary>
    /// Synthesizes a gamepad connected event.
    /// </summary>
    public void ConnectGamepad(int deviceId = 0, string name = "Test Gamepad")
    {
        getOrCreateGamepadState(deviceId);
        InputManager.HandleGamepadConnected(deviceId);
        rebuildQueues();
        InputManager.DispatchGamepadConnected(new GamepadConnectedEvent(deviceId, name));
    }

    /// <summary>
    /// Synthesizes a gamepad disconnected event.
    /// </summary>
    public void DisconnectGamepad(int deviceId = 0)
    {
        gamepadStates.Remove(deviceId);
        InputManager.HandleGamepadDisconnected(deviceId);
        rebuildQueues();
        InputManager.DispatchGamepadDisconnected(new GamepadDisconnectedEvent(deviceId));
    }

    /// <summary>
    /// Synthesizes a gamepad button press.
    /// </summary>
    public void PressGamepadButton(GamepadButton button, int deviceId = 0)
    {
        var state = getOrCreateGamepadState(deviceId);
        state.SetPressed(button, true);
        InputManager.HandleGamepadButtonDown(deviceId, button);
        rebuildQueues();
        InputManager.DispatchGamepadButtonDown(new GamepadButtonEvent(state.Clone(), button, isPressed: true));
    }

    /// <summary>
    /// Synthesizes a gamepad button release.
    /// </summary>
    public void ReleaseGamepadButton(GamepadButton button, int deviceId = 0)
    {
        var state = getOrCreateGamepadState(deviceId);
        state.SetPressed(button, false);
        InputManager.HandleGamepadButtonUp(deviceId, button);
        rebuildQueues();
        InputManager.DispatchGamepadButtonUp(new GamepadButtonEvent(state.Clone(), button, isPressed: false));
    }

    /// <summary>
    /// Synthesizes a complete gamepad button press then release.
    /// </summary>
    public void TapGamepadButton(GamepadButton button, int deviceId = 0)
    {
        PressGamepadButton(button, deviceId);
        ReleaseGamepadButton(button, deviceId);
    }

    /// <summary>
    /// Synthesizes a gamepad axis movement.
    /// </summary>
    /// <param name="axis">The axis to move.</param>
    /// <param name="value">Normalised value in [-1, 1]. Triggers use [0, 1].</param>
    /// <param name="deviceId">The gamepad device ID (default 0).</param>
    public void MoveGamepadAxis(GamepadAxis axis, float value, int deviceId = 0)
    {
        var state = getOrCreateGamepadState(deviceId);
        state.SetAxis(axis, value);
        InputManager.HandleGamepadAxis(deviceId, axis, value);
        rebuildQueues();
        InputManager.DispatchGamepadAxisMotion(new GamepadAxisEvent(state.Clone(), axis, value));
    }
}
