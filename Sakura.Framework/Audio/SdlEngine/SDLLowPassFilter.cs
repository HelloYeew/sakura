// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics.CodeAnalysis;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// A second-order Butterworth low-pass biquad for SDL backend's <see cref="ILowPassFilter"/>.
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
internal sealed class SDLLowPassFilter : ILowPassFilter
{
    /// <summary>
    /// Butterworth response — the same Q the BASS backend passes as <c>fQ</c>.
    /// </summary>
    private const double filter_q = 0.707;

    /// <summary>
    /// One coherent set of normalised biquad coefficients, published to the mix thread as a unit.
    /// </summary>
    internal sealed record Coefficients(float B0, float B1, float B2, float A1, float A2)
    {
        /// <summary>
        /// A pass-through, used when the cutoff is at or above what the sample rate can express.
        /// </summary>
        public static readonly Coefficients Bypass = new Coefficients(1f, 0f, 0f, 0f, 0f);
    }

    private readonly int sampleRate;
    private readonly int channels;
    private readonly Action? onDisposed;

    /// <summary>
    /// Transposed direct form II state, two elements per channel. TDF-II rather than DF-I because it
    /// needs half the state and is better behaved numerically at low cutoffs, where DF-I's delay line
    /// accumulates error against large-magnitude history.
    /// </summary>
    private readonly double[] state;

    private volatile Coefficients coefficients = Coefficients.Bypass;

    /// <summary>
    /// The coefficients in force, and whether they do anything at all.
    /// </summary>
    /// <remarks>
    /// Exposed so the native mix engine can be given the same numbers this class would apply itself:
    /// the cutoff-to-coefficient maths stays here, with one implementation and one set of tests, and
    /// <c>libsakura-audio</c> only applies what it is handed.
    /// </remarks>
    internal (bool Enabled, Coefficients Coefficients) CurrentCoefficients
    {
        get
        {
            var current = coefficients;
            return (!ReferenceEquals(current, Coefficients.Bypass), current);
        }
    }

    /// <summary>
    /// Raised on the update thread whenever <see cref="CurrentCoefficients"/> changes, so a native
    /// voice can republish them. A bypass is reported as a change like any other.
    /// </summary>
    internal event Action<bool, Coefficients>? CoefficientsChanged;

    /// <inheritdoc cref="ILowPassFilter.CutoffFrequency"/>
    public Reactive<double> CutoffFrequency { get; } = new Reactive<double>(ILowPassFilter.DefaultCutoffFrequency);

    /// <summary>
    /// Whether <see cref="Dispose"/> has run. The owning channel stops calling
    /// <see cref="Process"/> once this is set.
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <param name="sampleRate">The rate of the audio this filter will process, in Hz.</param>
    /// <param name="channels">Channel count of the interleaved buffers passed to <see cref="Process"/>.</param>
    /// <param name="onDisposed">Invoked on <see cref="Dispose"/> so the owner can detach the filter.</param>
    public SDLLowPassFilter(int sampleRate, int channels, Action? onDisposed = null)
    {
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));

        this.sampleRate = sampleRate;
        this.channels = channels;
        this.onDisposed = onDisposed;

        state = new double[channels * 2];

        updateCoefficients();
        CutoffFrequency.ValueChanged += _ => updateCoefficients();
    }

    private void updateCoefficients()
    {
        double maxCutoff = sampleRate / 2.0 - 1.0;
        double cutoff = Math.Clamp(CutoffFrequency.Value, 1.0, maxCutoff);

        // At the very top of the range, the filter does nothing audible, and the coefficient maths
        // degenerates as w0 approaches pi. Bypass rather than ring.
        if (cutoff >= maxCutoff)
        {
            coefficients = Coefficients.Bypass;
            CoefficientsChanged?.Invoke(false, Coefficients.Bypass);
            return;
        }

        double w0 = 2.0 * Math.PI * cutoff / sampleRate;
        double cosW0 = Math.Cos(w0);
        double alpha = Math.Sin(w0) / (2.0 * filter_q);

        double a0 = 1.0 + alpha;
        double b0 = (1.0 - cosW0) / 2.0;
        double b1 = 1.0 - cosW0;
        double b2 = b0;
        double a1 = -2.0 * cosW0;
        double a2 = 1.0 - alpha;

        var updated = new Coefficients(
            (float)(b0 / a0),
            (float)(b1 / a0),
            (float)(b2 / a0),
            (float)(a1 / a0),
            (float)(a2 / a0));

        coefficients = updated;
        CoefficientsChanged?.Invoke(true, updated);
    }

    /// <summary>
    /// Filters <paramref name="buffer"/> in place. Interleaved, with the channel count this filter
    /// was constructed for; each channel carries its own independent state.
    /// </summary>
    public void Process(Span<float> buffer)
    {
        if (IsDisposed)
            return;

        var c = coefficients;

        if (ReferenceEquals(c, Coefficients.Bypass))
            return;

        for (int i = 0; i + channels <= buffer.Length; i += channels)
        {
            for (int ch = 0; ch < channels; ch++)
            {
                int s = ch * 2;
                double input = buffer[i + ch];

                double output = c.B0 * input + state[s];
                state[s] = c.B1 * input - c.A1 * output + state[s + 1];
                state[s + 1] = c.B2 * input - c.A2 * output;

                buffer[i + ch] = (float)output;
            }
        }
    }

    /// <summary>
    /// Clears the filter's delay line without touching <see cref="CutoffFrequency"/>. Used on seek,
    /// where carrying history across a discontinuity would produce an audible transient.
    /// </summary>
    public void ClearState() => Array.Clear(state);

    public void Reset()
    {
        if (IsDisposed)
            return;

        CutoffFrequency.Value = ILowPassFilter.DefaultCutoffFrequency;
    }

    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;
        CutoffFrequency.UnbindAll();
        CoefficientsChanged = null;
        onDisposed?.Invoke();
    }
}
