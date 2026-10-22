// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Audio;

/// <summary>
/// The format a platform's audio output runs at natively. A stream at another rate is resampled on its way out,
/// and one whose buffer doesn't line up with the output's bursts can't take the platform's low-latency path.
/// </summary>
/// <param name="SampleRate">The output's rate, in hertz.</param>
/// <param name="FramesPerBurst">How many frames the output takes at a time, or 0 if the platform doesn't say.</param>
public readonly record struct NativeAudioOutput(int SampleRate, int FramesPerBurst);
