// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Timing;

namespace Sakura.Framework.Input;

/// <summary>
/// Fired when a gamepad axis value changes (stick movement or trigger press).
/// </summary>
public readonly struct GamepadAxisEvent
{
    /// <summary>
    /// Snapshot of the gamepad state at the moment the event was generated.
    /// </summary>
    public GamepadState GamepadState { get; }

    /// <summary>
    /// The axis that changed.
    /// </summary>
    public GamepadAxis Axis { get; }

    /// <summary>
    /// The new normalised axis value in the range [-1, 1].
    /// Trigger axes (LeftTrigger, RightTrigger) range from [0, 1].
    /// </summary>
    public float Value { get; }

    /// <summary>
    /// The time at which this event physically occurred, in milliseconds on the shared
    /// <see cref="TimeSource"/> timeline. <see cref="double.NaN"/> when not available.
    /// </summary>
    public double Timestamp { get; }

    public GamepadAxisEvent(GamepadState gamepadState, GamepadAxis axis, float value, double timestamp = double.NaN)
    {
        GamepadState = gamepadState;
        Axis = axis;
        Value = value;
        Timestamp = timestamp;
    }

    public override string ToString() => $"GamepadAxisEvent: Device={GamepadState.DeviceId}, Axis={Axis}, Value={Value:F3}";
}
