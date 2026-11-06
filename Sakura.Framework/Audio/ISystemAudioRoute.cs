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

    /// <summary>
    /// The part of <see cref="LatencyMilliseconds"/> spent in the system's own buffer, in milliseconds, which an output
    /// filling that buffer itself measures along with its own. 0 where the platform doesn't say.
    /// </summary>
    double BufferMilliseconds => 0;

    /// <summary>
    /// Whether <see cref="LatencyMilliseconds"/> goes all the way to the listener. False where it stops at the device and
    /// nothing says how long the driver, the hardware and the way past them take, as on Windows.
    /// </summary>
    bool ReachesListener => true;

    /// <summary>
    /// Says which device the sound plays on, for a platform whose route depends on it by the name the audio backend
    /// lists it under, or empty while the sound follows the system's default output. Said whenever it moves, on the
    /// thread that reads the route.
    /// </summary>
    /// <param name="name">The device's name, or empty for the system's default output.</param>
    void SetPlayingDevice(string name)
    {
    }
}
