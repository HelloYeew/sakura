// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Maths;

namespace Sakura.Framework.Input.Bindings;

/// <summary>
/// A unified key space that can represent keyboard keys, mouse buttons, scroll directions and
/// gamepad buttons, allowing a single <see cref="KeyCombination"/> to mix input devices.
/// </summary>
public enum InputKey
{
    /// <summary>No key.</summary>
    None = 0,

    #region Modifier

    /// <summary>Either shift key.</summary>
    Shift = 1,

    /// <summary>Either control key.</summary>
    Control = 2,

    /// <summary>Either alt key.</summary>
    Alt = 3,

    /// <summary>Either super/win/command key.</summary>
    Super = 4,

    #endregion

    #region Keyboard keys

    /// <summary>Start of the keyboard block. Keyboard keys are <c>KeyboardFirst + (int)Key</c>.</summary>
    KeyboardFirst = 1000,

    #endregion

    #region Mouse buttons

    /// <summary>Left mouse button.</summary>
    MouseLeft = 2000,

    /// <summary>Middle mouse button.</summary>
    MouseMiddle = 2001,

    /// <summary>Right mouse button.</summary>
    MouseRight = 2002,

    /// <summary>The fourth mouse button.</summary>
    MouseButton4 = 2003,

    /// <summary>The fifth mouse button.</summary>
    MouseButton5 = 2004,

    #endregion

    #region Scroll directions

    /// <summary>Scroll wheel moved up (away from the user).</summary>
    MouseWheelUp = 2100,

    /// <summary>Scroll wheel moved down (toward the user).</summary>
    MouseWheelDown = 2101,

    /// <summary>Scroll wheel moved left.</summary>
    MouseWheelLeft = 2102,

    /// <summary>Scroll wheel moved right.</summary>
    MouseWheelRight = 2103,

    #endregion


    #region Gamepad buttons

    /// <summary>Start of the gamepad button block.</summary>
    GamepadFirst = 3000,

    #endregion
}

/// <summary>
/// Conversion helpers between device-specific input enums and <see cref="InputKey"/>.
/// </summary>
public static class InputKeyExtensions
{
    /// <summary>
    /// Converts a keyboard <see cref="Key"/> into its <see cref="InputKey"/> representation.
    /// Side-specific modifier keys are preserved (use <see cref="ToLogicalModifier"/> to fold them).
    /// </summary>
    public static InputKey FromKey(Key key) => InputKey.KeyboardFirst + (int)key;

    /// <summary>
    /// Converts a <see cref="MouseButton"/> into its <see cref="InputKey"/> representation.
    /// </summary>
    public static InputKey FromMouseButton(MouseButton button) => button switch
    {
        MouseButton.Left => InputKey.MouseLeft,
        MouseButton.Middle => InputKey.MouseMiddle,
        MouseButton.Right => InputKey.MouseRight,
        MouseButton.Button4 => InputKey.MouseButton4,
        MouseButton.Button5 => InputKey.MouseButton5,
        _ => InputKey.None
    };

    /// <summary>
    /// Converts a <see cref="GamepadButton"/> into its <see cref="InputKey"/> representation.
    /// </summary>
    public static InputKey FromGamepadButton(GamepadButton button)
    {
        if (button == GamepadButton.Unknown)
            return InputKey.None;

        return InputKey.GamepadFirst + (int)button;
    }

    /// <summary>
    /// Maps a scroll delta to the momentary scroll <see cref="InputKey"/> values it represents.
    /// Returns <see cref="InputKey.None"/> components for axes that did not move.
    /// </summary>
    public static (InputKey vertical, InputKey horizontal) FromScrollDelta(Vector2 delta)
    {
        InputKey vertical = InputKey.None;
        InputKey horizontal = InputKey.None;

        if (delta.Y > 0)
            vertical = InputKey.MouseWheelUp;
        else if (delta.Y < 0)
            vertical = InputKey.MouseWheelDown;

        if (delta.X > 0)
            horizontal = InputKey.MouseWheelRight;
        else if (delta.X < 0)
            horizontal = InputKey.MouseWheelLeft;

        return (vertical, horizontal);
    }

    /// <summary>
    /// Whether the given <see cref="InputKey"/> represents a momentary scroll direction.
    /// </summary>
    public static bool IsScroll(this InputKey key)
        => key is InputKey.MouseWheelUp or InputKey.MouseWheelDown or InputKey.MouseWheelLeft or InputKey.MouseWheelRight;

    /// <summary>
    /// Whether the given <see cref="InputKey"/> is a physical (side-specific) modifier key,
    /// e.g. <see cref="InputKey"/> derived from <see cref="Key.ShiftLeft"/>.
    /// </summary>
    public static bool IsPhysicalModifier(this InputKey key)
    {
        var logical = ToLogicalModifier(key);
        return logical != InputKey.None;
    }

    /// <summary>
    /// Whether the given <see cref="InputKey"/> is a logical (side-agnostic) modifier.
    /// </summary>
    public static bool IsLogicalModifier(this InputKey key)
        => key is InputKey.Shift or InputKey.Control or InputKey.Alt or InputKey.Super;

    /// <summary>
    /// Whether the given <see cref="InputKey"/> is any kind of modifier (logical or physical).
    /// </summary>
    public static bool IsModifier(this InputKey key) => key.IsLogicalModifier() || key.IsPhysicalModifier();

    /// <summary>
    /// Folds a side-specific physical modifier key into its logical (side-agnostic) form.
    /// Returns <see cref="InputKey.None"/> if the key is not a physical modifier.
    /// </summary>
    public static InputKey ToLogicalModifier(this InputKey key)
    {
        if (key < InputKey.KeyboardFirst)
            return InputKey.None;

        var keyboard = (Key)(key - InputKey.KeyboardFirst);

        return keyboard switch
        {
            Key.ShiftLeft or Key.ShiftRight => InputKey.Shift,
            Key.ControlLeft or Key.ControlRight => InputKey.Control,
            Key.AltLeft or Key.AltRight => InputKey.Alt,
            Key.WinLeft or Key.WinRight => InputKey.Super,
            _ => InputKey.None
        };
    }
}
