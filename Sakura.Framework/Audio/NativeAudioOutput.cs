// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Audio;

/// <summary>
/// The format a platform's audio output runs at natively. A stream at another rate is resampled on its way out,
/// and one whose buffer doesn't line up with the output's bursts can't take the platform's low-latency path.
/// </summary>
/// <param name="SampleRate">The output's rate, in hertz.</param>
/// <param name="FramesPerBurst">How many frames the output takes at a time, or 0 if the platform doesn't say.</param>
/// <param name="DescribeDevice">
/// Names an output device from the ID the platform's own audio gives it (AAudio's, on Android), as
/// <see cref="IAudioManager.PlayingDevice"/> shows it: "Bluetooth: Galaxy Buds". Null where the platform can't say.
/// Called on the audio thread, and only when the device changes.
/// </param>
public readonly record struct NativeAudioOutput(int SampleRate, int FramesPerBurst, Func<int, string>? DescribeDevice = null);
