// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Audio;

/// <summary>
/// Where in its way out a part of <see cref="IAudioManager.OutputLatencyMs"/> holds the sound.
/// </summary>
public enum AudioLatencyPartKind
{
    /// <summary>
    /// Mixed, and waiting in the app's queue to be handed to the device.
    /// </summary>
    Queue,

    /// <summary>
    /// In the app's own device buffers.
    /// </summary>
    AppBuffer,

    /// <summary>
    /// Past the app's own buffers: the system's mixer and buffers, the device, and the way from it to the listener, as
    /// far as the platform says. What a player knows as their device and their system. Every output has this part,
    /// with a figure or without.
    /// </summary>
    System,

    /// <summary>
    /// After the device, where the system part stops at the device and nothing says how long the rest takes. Never has
    /// a figure.
    /// </summary>
    AfterDevice
}
