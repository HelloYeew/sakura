// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Audio;

/// <summary>
/// Where a platform's own audio takes the sound once it leaves the app's device buffer: through the system's buffer
/// and out of the speaker, wired headphones or a Bluetooth device, each adding its own delay. For a platform that says,
/// so the sound's delay counts all of it and the output can be told apart by name. Read on the audio thread.
/// </summary>
public interface ISystemAudioRoute
{
    /// <summary>
    /// The output as the player knows it, as <see cref="IAudioManager.PlayingDevice"/> shows it: "Speaker",
    /// "Bluetooth: AirPods Pro". Empty where the platform doesn't say.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// How long the sound takes from leaving the app's device buffer to being heard, in milliseconds.
    /// </summary>
    double LatencyMilliseconds { get; }
}
