// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Audio;

/// <summary>
/// A snapshot of the amplitude data for an audio channel at a point in time.
/// Carries the overall left/right peak levels plus a frequency spectrum
/// (an FFT of the currently playing audio), suitable for driving visualisers.
/// </summary>
public readonly struct ChannelAmplitudes
{
    /// <summary>
    /// The number of frequency-amplitude bins exposed by <see cref="FrequencyAmplitudes"/>.
    /// </summary>
    public const int AMPLITUDES_SIZE = 256;

    /// <summary>
    /// The current peak amplitude of the left channel (0.0 to 1.0).
    /// </summary>
    public readonly float AmplitudeLeft;

    /// <summary>
    /// The current peak amplitude of the right channel (0.0 to 1.0).
    /// </summary>
    public readonly float AmplitudeRight;

    /// <summary>
    /// The frequency spectrum of the channel, as <see cref="AMPLITUDES_SIZE"/> bins,
    /// each in the range 0.0 to 1.0. Backends that do not provide spectrum data
    /// (e.g. headless) return an empty span.
    /// </summary>
    public readonly ReadOnlyMemory<float> FrequencyAmplitudes;

    public ChannelAmplitudes(float amplitudeLeft, float amplitudeRight, ReadOnlyMemory<float> frequencyAmplitudes)
    {
        AmplitudeLeft = amplitudeLeft;
        AmplitudeRight = amplitudeRight;
        FrequencyAmplitudes = frequencyAmplitudes;
    }

    /// <summary>
    /// The maximum of the left and right peak amplitudes.
    /// </summary>
    public float Maximum => Math.Max(AmplitudeLeft, AmplitudeRight);

    /// <summary>
    /// The average of the left and right peak amplitudes.
    /// </summary>
    public float Average => (AmplitudeLeft + AmplitudeRight) / 2f;

    private static readonly float[] empty_frequency_amplitudes = new float[AMPLITUDES_SIZE];

    /// <summary>
    /// An all-zero <see cref="ChannelAmplitudes"/> with a correctly sized (silent) frequency spectrum.
    /// </summary>
    public static ChannelAmplitudes Empty { get; } = new ChannelAmplitudes(0f, 0f, empty_frequency_amplitudes);
}
