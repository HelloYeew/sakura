// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;

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

    public ManualInputManager()
    {
        RelativeSizeAxes = Axes.Both;
        Size = new Vector2(1);
    }

    public override bool HandleNonPositionalInput => UseParentInput && base.HandleNonPositionalInput;
    public override bool HandlePositionalInput => UseParentInput && base.HandlePositionalInput;


    public override bool OnMouseMove(MouseEvent e)
    {
        if (!UseParentInput)
            return true;

        currentMouseState.Position = e.MouseState.Position;
        InputManager.HandleMouseMove(e.MouseState.Position);
        rebuildQueues();
        return InputManager.DispatchMouseMove(e);
    }

    public override bool OnMouseDown(MouseButtonEvent e)
    {
        if (!UseParentInput)
            return true;

        currentMouseState.SetPressed(e.Button, true);
        InputManager.HandleMouseDown(e.Button, e.ScreenSpaceMousePosition);
        rebuildQueues();
        BeginMouseDownFocusTracking();
        bool handled = InputManager.DispatchMouseDown(e);
        if (!WasFocusClaimedByLastClick)
            ChangeFocus(null);
        return handled;
    }

    public override bool OnMouseUp(MouseButtonEvent e)
    {
        if (!UseParentInput)
            return true;

        currentMouseState.SetPressed(e.Button, false);
        InputManager.HandleMouseUp(e.Button, e.ScreenSpaceMousePosition);
        rebuildQueues();
        return InputManager.DispatchMouseUp(e);
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
    private bool dispatchKeyDownWithFocus(KeyEvent e)
    {
        var focused = InputManager.FocusedDrawable;
        if (focused != null && focused.IsLoaded && focused.IsAlive)
        {
            if (focused.OnKeyDown(e))
                return true;
        }

        return InputManager.DispatchKeyDown(e);
    }

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
    /// Synthesizes a double click at the current mouse position.
    /// </summary>
    public void DoubleClick(MouseButton button)
    {
        currentMouseState.SetPressed(button, true);
        InputManager.HandleMouseDown(button, currentMouseState.Position);
        rebuildQueues();
        BeginMouseDownFocusTracking();
        InputManager.DispatchMouseDown(new MouseButtonEvent(currentMouseState, button, 2));
        if (!WasFocusClaimedByLastClick)
            ChangeFocus(null);

        currentMouseState.SetPressed(button, false);
        InputManager.HandleMouseUp(button, currentMouseState.Position);
        rebuildQueues();
        InputManager.DispatchMouseUp(new MouseButtonEvent(currentMouseState, button, 2));
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
    public void PressButton(MouseButton button)
    {
        currentMouseState.SetPressed(button, true);
        InputManager.HandleMouseDown(button, currentMouseState.Position);
        rebuildQueues();

        // Begin focus tracking so we can detect whether a focusable claimed focus.
        BeginMouseDownFocusTracking();

        InputManager.DispatchMouseDown(new MouseButtonEvent(currentMouseState, button, 1));

        // If nothing claimed focus during this click, clear focus — same logic as App.OnMouseDown.
        if (!WasFocusClaimedByLastClick)
            ChangeFocus(null);
    }

    /// <summary>
    /// Synthesizes a mouse button release at the current mouse position.
    /// </summary>
    /// <param name="button">The mouse button to release.</param>
    public void ReleaseButton(MouseButton button)
    {
        currentMouseState.SetPressed(button, false);
        InputManager.HandleMouseUp(button, currentMouseState.Position);
        rebuildQueues();
        InputManager.DispatchMouseUp(new MouseButtonEvent(currentMouseState, button, 1));
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
