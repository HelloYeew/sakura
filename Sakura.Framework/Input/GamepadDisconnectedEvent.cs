// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Input;

/// <summary>
/// Fired when a gamepad is unplugged or otherwise becomes unavailable.
/// </summary>
public readonly struct GamepadDisconnectedEvent
{
    /// <summary>
    /// The SDL instance ID of the gamepad that was removed.
    /// </summary>
    public int DeviceId { get; }

    public GamepadDisconnectedEvent(int deviceId)
    {
        DeviceId = deviceId;
    }

    public override string ToString() => $"GamepadDisconnectedEvent: Device={DeviceId}";
}
