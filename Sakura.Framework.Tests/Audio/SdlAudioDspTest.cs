// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Threading;
using NUnit.Framework;
using Sakura.Framework.Audio;
using Sakura.Framework.Audio.SdlEngine;

namespace Sakura.Framework.Tests.Audio;

/// <summary>
/// Signal-level coverage for the SDL backend's DSP building blocks math test
/// </summary>
[TestFixture]
public class SdlAudioDspTest
{
    private const int sample_rate = 44100;

    /// <summary>
    /// A sine placed exactly on bin <paramref name="bin"/> of a <see cref="AudioFft.FFT_SIZE"/>
    /// transform, so there is no scalloping loss to account for.
    /// </summary>
    private static float[] binCentredSine(int bin, float amplitude)
    {
        float[] buffer = new float[AudioFft.FFT_SIZE];

        for (int i = 0; i < buffer.Length; i++)
            buffer[i] = amplitude * MathF.Sin(2f * MathF.PI * bin * i / AudioFft.FFT_SIZE);

        return buffer;
    }

    private static float[] sine(int sampleCount, double frequency, float amplitude, int channels = 1)
    {
        float[] buffer = new float[sampleCount * channels];

        for (int i = 0; i < sampleCount; i++)
        {
            float value = amplitude * MathF.Sin((float)(2.0 * Math.PI * frequency * i / sample_rate));

            for (int ch = 0; ch < channels; ch++)
                buffer[i * channels + ch] = value;
        }

        return buffer;
    }

    [Test]
    public void Fft_Silence_ProducesNoEnergy()
    {
        float[] bins = new float[AudioFft.BIN_COUNT];
        new AudioFft().Compute(new float[AudioFft.FFT_SIZE], bins);

        Assert.That(bins, Is.All.Zero);
    }

    [TestCase(4)]
    [TestCase(17)]
    [TestCase(64)]
    public void Fft_SinePeaksInItsOwnBin(int bin)
    {
        float[] bins = new float[AudioFft.BIN_COUNT];
        new AudioFft().Compute(binCentredSine(bin, 0.5f), bins);

        int peakBin = 0;

        for (int i = 0; i < bins.Length; i++)
        {
            if (bins[i] > bins[peakBin])
                peakBin = i;
        }

        Assert.That(peakBin, Is.EqualTo(bin));
    }

    /// <summary>
    /// The scaling contract: a bin-centred sine reads back at its own peak amplitude.
    /// </summary>
    [TestCase(0.25f)]
    [TestCase(0.5f)]
    [TestCase(1.0f)]
    public void Fft_RecoversSineAmplitude(float amplitude)
    {
        float[] bins = new float[AudioFft.BIN_COUNT];
        new AudioFft().Compute(binCentredSine(20, amplitude), bins);

        // A Hann window puts the tone's main lobe across three bins in a 0.25 / 0.5 / 0.25 split,
        // so the three together sum to 2A while the centre bin alone carries A. The centre is the
        // scaling contract; the shoulders are checked here so the split itself cannot drift.
        using (Assert.EnterMultipleScope())
        {
            Assert.That(bins[20], Is.EqualTo(amplitude).Within(1).Percent);
            Assert.That(bins[19], Is.EqualTo(amplitude / 2f).Within(1).Percent);
            Assert.That(bins[21], Is.EqualTo(amplitude / 2f).Within(1).Percent);
        }
    }

    [Test]
    public void Fft_EnergyStaysLocalToTheTone()
    {
        float[] bins = new float[AudioFft.BIN_COUNT];
        new AudioFft().Compute(binCentredSine(40, 1.0f), bins);

        float faraway = 0;

        for (int i = 0; i < bins.Length; i++)
        {
            if (Math.Abs(i - 40) > 3)
                faraway = Math.Max(faraway, bins[i]);
        }

        Assert.That(faraway, Is.LessThan(bins[40] * 0.01f), "Spectral leakage far exceeds what a Hann window should produce.");
    }

    [Test]
    public void LowPass_PassesDcUnchanged()
    {
        var filter = new SDLLowPassFilter(sample_rate, 1) { CutoffFrequency = { Value = 500 } };

        float[] buffer = new float[4096];
        Array.Fill(buffer, 0.5f);
        filter.Process(buffer);

        // The first samples are the filter's step response settling; the tail is the steady state.
        Assert.That(buffer[^1], Is.EqualTo(0.5f).Within(0.001f));
    }

    [Test]
    public void LowPass_AttenuatesAboveCutoffAndPassesBelow()
    {
        const int samples = 8192;

        float peakOf(double frequency, double cutoff)
        {
            var filter = new SDLLowPassFilter(sample_rate, 1) { CutoffFrequency = { Value = cutoff } };
            float[] buffer = sine(samples, frequency, 1.0f);
            filter.Process(buffer);

            float peak = 0;

            // Skip the settling transient and measure the steady state.
            for (int i = samples / 2; i < samples; i++)
                peak = Math.Max(peak, Math.Abs(buffer[i]));

            return peak;
        }

        float passed = peakOf(200, 2000);
        float stopped = peakOf(12000, 2000);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(passed, Is.EqualTo(1.0f).Within(0.05f), "A tone well below cutoff should pass essentially untouched.");
            Assert.That(stopped, Is.LessThan(0.05f), "A tone well above cutoff should be strongly attenuated.");
        }
    }

    /// <summary>
    /// At the cutoff frequency a Butterworth low-pass is 3 dB down — about 0.707 of the input.
    /// This is what pins Q to the same value the BASS backend uses.
    /// </summary>
    [Test]
    public void LowPass_IsThreeDecibelsDownAtCutoff()
    {
        const int samples = 16384;
        const double cutoff = 1000;

        var filter = new SDLLowPassFilter(sample_rate, 1) { CutoffFrequency = { Value = cutoff } };
        float[] buffer = sine(samples, cutoff, 1.0f);
        filter.Process(buffer);

        float peak = 0;

        for (int i = samples / 2; i < samples; i++)
            peak = Math.Max(peak, Math.Abs(buffer[i]));

        Assert.That(peak, Is.EqualTo(0.707f).Within(0.02f));
    }

    [Test]
    public void LowPass_ClampsCutoffAboveNyquistInsteadOfExploding()
    {
        var filter = new SDLLowPassFilter(sample_rate, 2) { CutoffFrequency = { Value = 500_000 } };

        float[] buffer = sine(2048, 1000, 0.5f, channels: 2);
        float[] unfiltered = (float[])buffer.Clone();
        filter.Process(buffer);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(buffer, Is.All.Matches<float>(float.IsFinite));

            // Beyond what the rate can express the filter bypasses, so the signal survives intact.
            Assert.That(buffer, Is.EqualTo(unfiltered));
        }
    }

    [Test]
    public void LowPass_ChannelsAreFilteredIndependently()
    {
        var filter = new SDLLowPassFilter(sample_rate, 2) { CutoffFrequency = { Value = 300 } };

        // Left carries a tone, right is silent. Any bleed means the two channels share state.
        float[] buffer = new float[4096];

        for (int i = 0; i < buffer.Length / 2; i++)
            buffer[i * 2] = MathF.Sin((float)(2.0 * Math.PI * 100 * i / sample_rate));

        filter.Process(buffer);

        float rightPeak = 0;

        for (int i = 0; i < buffer.Length / 2; i++)
            rightPeak = Math.Max(rightPeak, Math.Abs(buffer[i * 2 + 1]));

        Assert.That(rightPeak, Is.Zero);
    }

    [Test]
    public void LowPass_ResetRestoresTheDefaultCutoff()
    {
        var filter = new SDLLowPassFilter(sample_rate, 2);
        filter.CutoffFrequency.Value = 500;

        filter.Reset();

        Assert.That(filter.CutoffFrequency.Value, Is.EqualTo(ILowPassFilter.DefaultCutoffFrequency));
    }

    [Test]
    public void LowPass_DisposeDetachesAndStopsProcessing()
    {
        bool detached = false;
        var filter = new SDLLowPassFilter(sample_rate, 1, () => detached = true) { CutoffFrequency = { Value = 100 } };

        filter.Dispose();

        float[] buffer = sine(512, 10000, 1.0f);
        float[] expected = (float[])buffer.Clone();
        filter.Process(buffer);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(detached, Is.True);
            Assert.That(filter.IsDisposed, Is.True);
            Assert.That(buffer, Is.EqualTo(expected), "A disposed filter must leave audio untouched.");
        }

        Assert.DoesNotThrow(() => filter.Dispose());
    }

    [Test]
    public void Tap_ReportsPerChannelPeaks()
    {
        var tap = new AmplitudeTap();

        // Left louder than right, so a swapped pair would be obvious.
        float[] block = new float[512];

        for (int i = 0; i < block.Length; i += 2)
        {
            block[i] = 0.8f;
            block[i + 1] = -0.3f;
        }

        tap.Feed(block);
        var amplitudes = tap.Read();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(amplitudes.AmplitudeLeft, Is.EqualTo(0.8f).Within(0.001f));
            Assert.That(amplitudes.AmplitudeRight, Is.EqualTo(0.3f).Within(0.001f), "Peak should be taken on magnitude, not signed value.");
            Assert.That(amplitudes.FrequencyAmplitudes.Length, Is.EqualTo(ChannelAmplitudes.AMPLITUDES_SIZE));
        }
    }

    [Test]
    public void Tap_WithNoAudioReportsSilence()
    {
        var amplitudes = new AmplitudeTap().Read();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(amplitudes.AmplitudeLeft, Is.Zero);
            Assert.That(amplitudes.AmplitudeRight, Is.Zero);
            Assert.That(amplitudes.FrequencyAmplitudes.ToArray(), Is.All.Zero);
        }
    }

    [Test]
    public void Tap_HoldsPeakAcrossAReadThatSawNoAudio()
    {
        var tap = new AmplitudeTap();

        // How the mix thread actually delivers audio: one device callback's worth in a burst, then a
        // gap longer than the reader's cache interval. A reader that happens to land in the gap must
        // still see the level rather than reporting silence mid-song.
        float[] burst = new float[1024];
        Array.Fill(burst, 0.75f);

        tap.Feed(burst);
        Assert.That(tap.Read().AmplitudeLeft, Is.EqualTo(0.75f).Within(0.001f));

        Thread.Sleep(20);

        var amplitudes = tap.Read();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(amplitudes.AmplitudeLeft, Is.EqualTo(0.75f).Within(0.001f));
            Assert.That(amplitudes.AmplitudeRight, Is.EqualTo(0.75f).Within(0.001f));
        }
    }

    [Test]
    public void Tap_PeakRollsOffAsQuieterAudioPassesThrough()
    {
        var tap = new AmplitudeTap();

        float[] loud = new float[64];
        Array.Fill(loud, 0.9f);
        tap.Feed(loud);

        // Enough quiet audio to push the loud segment out of the peak window entirely.
        float[] quiet = new float[8192];
        Array.Fill(quiet, 0.1f);
        tap.Feed(quiet);

        Thread.Sleep(20);

        Assert.That(tap.Read().AmplitudeLeft, Is.EqualTo(0.1f).Within(0.001f), "The peak window must advance with the audio fed through it.");
    }

    [Test]
    public void Tap_ResetClearsMeteringAndSpectrum()
    {
        var tap = new AmplitudeTap();

        float[] block = new float[AudioFft.FFT_SIZE * 2];
        Array.Fill(block, 0.9f);
        tap.Feed(block);
        tap.Read();

        tap.Reset();
        var amplitudes = tap.Read();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(amplitudes.AmplitudeLeft, Is.Zero);
            Assert.That(amplitudes.AmplitudeRight, Is.Zero);
            Assert.That(amplitudes.FrequencyAmplitudes.ToArray(), Is.All.Zero);
        }
    }
}
