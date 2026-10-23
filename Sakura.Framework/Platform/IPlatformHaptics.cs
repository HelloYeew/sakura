// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Platform;

/// <summary>
/// The device's vibration motor, for feedback the player feels: a tap as a note is hit, a tick as a picker moves on.
/// Phones have one (iOS's Taptic Engine, Android's vibrator); elsewhere, and on a device without one, every member does
/// nothing. Let the player turn it off: some find it distracting, and it costs battery.
/// </summary>
public interface IPlatformHaptics
{
    /// <summary>
    /// Whether the device can play feedback. False on desktops, in simulators and emulators, and on devices without a
    /// motor.
    /// </summary>
    bool IsSupported => false;

    /// <summary>
    /// Gets the motor ready, for a screen about to use it often, such as gameplay: otherwise the first feedback may come a
    /// little late, as the motor starts. Cheap to call again.
    /// </summary>
    void Prepare()
    {
    }

    /// <summary>
    /// Plays one of the system's kinds of feedback, at once. From any thread.
    /// </summary>
    void Play(HapticFeedback feedback)
    {
    }

    /// <summary>
    /// Plays a single tap, at once, for feedback that varies, such as with a hit's judgement. From any thread.
    /// </summary>
    /// <param name="intensity">How strong, from 0 (barely felt) to 1.</param>
    /// <param name="sharpness">
    /// How crisp, from 0 (a dull thud) to 1 (a sharp click). Ignored where the motor can't vary it, as on most Android
    /// phones.
    /// </param>
    void PlayTap(float intensity, float sharpness)
    {
    }
}

/// <summary>
/// The haptics of a platform that has none.
/// </summary>
internal sealed class NoPlatformHaptics : IPlatformHaptics
{
}
