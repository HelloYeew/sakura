// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;

namespace Sakura.Framework.Input;

/// <summary>
/// Snapshot of a single gamepad's state (buttons held and axis values).
/// </summary>
public class GamepadState
{
    /// <summary>
    /// The SDL instance ID of the gamepad this state belongs to.
    /// </summary>
    public int DeviceId { get; set; }

    private readonly HashSet<GamepadButton> pressedButtons = new HashSet<GamepadButton>();
    private readonly Dictionary<GamepadAxis, float> axisValues = new Dictionary<GamepadAxis, float>();

    /// <summary>
    /// The buttons currently held on this gamepad.
    /// </summary>
    public IReadOnlyCollection<GamepadButton> PressedButtons => pressedButtons;

    /// <summary>
    /// Returns true if <paramref name="button"/> is currently held.
    /// </summary>
    public bool IsPressed(GamepadButton button) => pressedButtons.Contains(button);

    /// <summary>
    /// Returns the normalised value [-1, 1] of <paramref name="axis"/> (0 if not reported).
    /// </summary>
    public float GetAxis(GamepadAxis axis) => axisValues.TryGetValue(axis, out float v) ? v : 0f;

    /// <summary>
    /// Updates the pressed state of <paramref name="button"/>.
    /// </summary>
    public void SetPressed(GamepadButton button, bool pressed)
    {
        if (pressed)
            pressedButtons.Add(button);
        else
            pressedButtons.Remove(button);
    }

    /// <summary>
    /// Updates the normalised value of <paramref name="axis"/>.
    /// </summary>
    public void SetAxis(GamepadAxis axis, float value) => axisValues[axis] = value;

    /// <summary>
    /// Returns a shallow copy of this state.
    /// </summary>
    public GamepadState Clone()
    {
        var clone = new GamepadState
        {
            DeviceId = DeviceId
        };
        foreach (var b in pressedButtons)
            clone.pressedButtons.Add(b);
        foreach (var kv in axisValues)
            clone.axisValues[kv.Key] = kv.Value;
        return clone;
    }

    public override string ToString() => $"GamepadState: Device={DeviceId}, Pressed=[{string.Join(", ", pressedButtons)}]";
}
