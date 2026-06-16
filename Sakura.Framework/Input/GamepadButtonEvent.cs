// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Timing;

namespace Sakura.Framework.Input;

/// <summary>
/// Fired when a gamepad button is pressed or released.
/// </summary>
public readonly struct GamepadButtonEvent
{
    /// <summary>
    /// Snapshot of the gamepad state at the moment the event was generated.
    /// </summary>
    public GamepadState GamepadState { get; }

    /// <summary>
    /// The button that triggered this event.
    /// </summary>
    public GamepadButton Button { get; }

    /// <summary>
    /// Whether the button was pressed (<c>true</c>) or released (<c>false</c>).
    /// </summary>
    public bool IsPressed { get; }

    /// <summary>
    /// The time at which this event physically occurred, in milliseconds on the shared
    /// <see cref="TimeSource"/> timeline. <see cref="double.NaN"/> when not available.
    /// </summary>
    public double Timestamp { get; }

    public GamepadButtonEvent(GamepadState gamepadState, GamepadButton button, bool isPressed, double timestamp = double.NaN)
    {
        GamepadState = gamepadState;
        Button = button;
        IsPressed = isPressed;
        Timestamp = timestamp;
    }

    public override string ToString() => $"GamepadButtonEvent: Device={GamepadState.DeviceId}, Button={Button}, Pressed={IsPressed}";
}
