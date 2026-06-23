// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using Sakura.Framework.Input.Bindings;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Input;

/// <summary>
/// The authoritative snapshot of "what is currently pressed / where is the mouse" for an
/// <see cref="InputManager"/>
/// </summary>
public class InputState
{
    /// <summary>
    /// The most recent mouse position in screen-space coordinates.
    /// </summary>
    public Vector2 MousePosition { get; internal set; }

    private readonly HashSet<MouseButton> pressedMouseButtons = new HashSet<MouseButton>();
    private readonly HashSet<Key> pressedKeys = new HashSet<Key>();
    private readonly Dictionary<int, GamepadState> gamepads = new Dictionary<int, GamepadState>();

    /// <summary>
    /// The mouse buttons currently held.
    /// </summary>
    public IReadOnlyCollection<MouseButton> PressedMouseButtons => pressedMouseButtons;

    /// <summary>
    /// The keyboard keys currently held (side-specific, e.g. <see cref="Key.ShiftLeft"/>).
    /// </summary>
    public IReadOnlyCollection<Key> PressedKeys => pressedKeys;

    /// <summary>
    /// The currently connected gamepads, keyed by device id.
    /// </summary>
    public IReadOnlyDictionary<int, GamepadState> Gamepads => new ReadOnlyDictionary<int, GamepadState>(gamepads);

    /// <summary>
    /// The active logical modifiers, derived from <see cref="PressedKeys"/>.
    /// </summary>
    public KeyModifiers Modifiers { get; private set; }

    public bool IsPressed(MouseButton button) => pressedMouseButtons.Contains(button);

    public bool IsPressed(Key key) => pressedKeys.Contains(key);

    /// <summary>
    /// The set of currently-held inputs expressed in the unified <see cref="InputKey"/> space
    /// (keyboard keys folded to logical modifiers where applicable, plus mouse buttons). This is the
    /// representation <c>KeyCombination</c> matches against; later phases consume it directly.
    /// </summary>
    public IReadOnlyList<InputKey> GetPressedInputKeys()
    {
        var result = new List<InputKey>();

        foreach (var key in pressedKeys)
        {
            var input = InputKeyExtensions.FromKey(key);
            var logical = input.ToLogicalModifier();
            result.Add(logical != InputKey.None ? logical : input);
        }

        foreach (var button in pressedMouseButtons)
        {
            var input = InputKeyExtensions.FromMouseButton(button);
            if (input != InputKey.None)
                result.Add(input);
        }

        return result;
    }

    #region Mutation (manager-only)

    internal void SetMousePosition(Vector2 position) => MousePosition = position;

    internal void SetMouseButton(MouseButton button, bool pressed)
    {
        if (pressed)
            pressedMouseButtons.Add(button);
        else
            pressedMouseButtons.Remove(button);
    }

    internal void SetKey(Key key, bool pressed)
    {
        if (pressed)
            pressedKeys.Add(key);
        else
            pressedKeys.Remove(key);

        recomputeModifiers();
    }

    internal void SetGamepadButton(int deviceId, GamepadButton button, bool pressed)
        => getOrAddGamepad(deviceId).SetPressed(button, pressed);

    internal void SetGamepadAxis(int deviceId, GamepadAxis axis, float value)
        => getOrAddGamepad(deviceId).SetAxis(axis, value);

    internal void AddGamepad(int deviceId) => getOrAddGamepad(deviceId);

    internal void RemoveGamepad(int deviceId) => gamepads.Remove(deviceId);

    private GamepadState getOrAddGamepad(int deviceId)
    {
        if (!gamepads.TryGetValue(deviceId, out var state))
        {
            state = new GamepadState { DeviceId = deviceId };
            gamepads[deviceId] = state;
        }

        return state;
    }

    private void recomputeModifiers()
    {
        var modifiers = KeyModifiers.None;

        foreach (var key in pressedKeys)
        {
            switch (key)
            {
                case Key.ShiftLeft:
                case Key.ShiftRight:
                    modifiers |= KeyModifiers.Shift;
                    break;

                case Key.ControlLeft:
                case Key.ControlRight:
                    modifiers |= KeyModifiers.Control;
                    break;

                case Key.AltLeft:
                case Key.AltRight:
                    modifiers |= KeyModifiers.Alt;
                    break;
            }
        }

        Modifiers = modifiers;
    }

    #endregion
}
