// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Input;

/// <summary>
/// Fired when a gamepad is plugged in or otherwise becomes available.
/// </summary>
public readonly struct GamepadConnectedEvent
{
    /// <summary>
    /// The SDL instance ID of the newly connected gamepad.
    /// </summary>
    public int DeviceId { get; }

    /// <summary>
    /// Human-readable name reported by SDL (may be empty if unavailable).
    /// </summary>
    public string Name { get; }

    public GamepadConnectedEvent(int deviceId, string name)
    {
        DeviceId = deviceId;
        Name = name;
    }

    public override string ToString() => $"GamepadConnectedEvent: Device={DeviceId}, Name=\"{Name}\"";
}
