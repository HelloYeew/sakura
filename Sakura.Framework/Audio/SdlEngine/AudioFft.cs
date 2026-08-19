// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Numerics;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// A fixed-size real-input radix-2 FFT producing the magnitude spectrum a visualizer consumes.
/// </summary>
internal sealed class AudioFft
{
    /// <summary>
    /// The transform size. Must stay a power of two. Size reference from the original BASS FFT implementation.
    /// </summary>
    public const int FFT_SIZE = 512;

    /// <summary>
    /// The number of magnitude bins produced, covering DC up to (but excluding) Nyquist.
    /// </summary>
    public const int BIN_COUNT = ChannelAmplitudes.AMPLITUDES_SIZE;

    private static readonly float[] window = buildHannWindow();
    private static readonly int[] bit_reversal = buildBitReversal();

    private readonly float[] real = new float[FFT_SIZE];
    private readonly float[] imaginary = new float[FFT_SIZE];

    static AudioFft()
    {
        // A 512-point transform must produce exactly 256 bins for the spectrum to line up with what
        // ChannelAmplitudes promises; if either constant moves, this stops being true.
        if (FFT_SIZE / 2 != BIN_COUNT)
            throw new InvalidOperationException($"{nameof(FFT_SIZE)} must be twice {nameof(BIN_COUNT)}.");
    }

    private static float[] buildHannWindow()
    {
        float[] result = new float[FFT_SIZE];

        for (int i = 0; i < FFT_SIZE; i++)
            result[i] = (float)(0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (FFT_SIZE - 1))));

        return result;
    }

    private static int[] buildBitReversal()
    {
        int[] result = new int[FFT_SIZE];
        int bits = BitOperations.Log2(FFT_SIZE);

        for (int i = 0; i < FFT_SIZE; i++)
        {
            int reversed = 0;

            for (int bit = 0; bit < bits; bit++)
            {
                if ((i & (1 << bit)) != 0)
                    reversed |= 1 << (bits - 1 - bit);
            }

            result[i] = reversed;
        }

        return result;
    }

    /// <summary>
    /// Transforms <paramref name="samples"/> and writes <see cref="BIN_COUNT"/> magnitudes into
    /// <paramref name="destination"/>.
    /// </summary>
    /// <param name="samples">
    /// Mono input. Fewer than <see cref="FFT_SIZE"/> samples are zero-padded; more are truncated.
    /// </param>
    /// <param name="destination">Receives the magnitudes. Must hold at least <see cref="BIN_COUNT"/> floats.</param>
    /// <remarks>
    /// Magnitudes are scaled so a bin-centred full-scale sine reads back at its own peak amplitude —
    /// the <c>2 / (N * coherentGain)</c> correction, which for Hann is <c>4 / N</c>. Values are not
    /// clipped to 1.0: input above unity is normal (see <c>FFmpegAudioDecoderTest</c>) and clamping
    /// here would hide it from a visualiser. Exact scale parity with BASS is a Phase 5 A/B item.
    /// </remarks>
    public void Compute(ReadOnlySpan<float> samples, Span<float> destination)
    {
        if (destination.Length < BIN_COUNT)
            throw new ArgumentException($"Destination must hold at least {BIN_COUNT} bins.", nameof(destination));

        int count = Math.Min(samples.Length, FFT_SIZE);

        // Window into bit-reversed order in one pass, so the butterflies below can run in place.
        Array.Clear(real);
        Array.Clear(imaginary);

        for (int i = 0; i < count; i++)
            real[bit_reversal[i]] = samples[i] * window[i];

        for (int size = 2; size <= FFT_SIZE; size <<= 1)
        {
            int half = size / 2;
            double angleStep = -2.0 * Math.PI / size;

            for (int start = 0; start < FFT_SIZE; start += size)
            {
                for (int k = 0; k < half; k++)
                {
                    double angle = angleStep * k;
                    float twiddleReal = (float)Math.Cos(angle);
                    float twiddleImaginary = (float)Math.Sin(angle);

                    int even = start + k;
                    int odd = even + half;

                    float oddReal = real[odd] * twiddleReal - imaginary[odd] * twiddleImaginary;
                    float oddImaginary = real[odd] * twiddleImaginary + imaginary[odd] * twiddleReal;

                    real[odd] = real[even] - oddReal;
                    imaginary[odd] = imaginary[even] - oddImaginary;
                    real[even] += oddReal;
                    imaginary[even] += oddImaginary;
                }
            }
        }

        const float scale = 4f / FFT_SIZE;

        for (int i = 0; i < BIN_COUNT; i++)
            destination[i] = MathF.Sqrt(real[i] * real[i] + imaginary[i] * imaginary[i]) * scale;
    }
}
