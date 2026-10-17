// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Audio.SdlEngine;

namespace Sakura.Framework.Tests.Audio;

/// <summary>
/// The WSOLA time-stretch behind <c>IAudioChannel.Tempo</c> on the SDL engines, and the
/// <see cref="TempoPcmSource"/> that puts it in front of the managed mixer.
/// </summary>
[TestFixture]
public class TimeStretcherTest
{
    private const int rate = 44100;
    private const int channels = 2;

    private static float[] sine(double frequency, int frames, double amplitude = 0.5)
    {
        float[] samples = new float[frames * channels];

        for (int frame = 0; frame < frames; frame++)
        {
            float value = (float)(amplitude * Math.Sin(2 * Math.PI * frequency * frame / rate));

            for (int c = 0; c < channels; c++)
                samples[frame * channels + c] = value;
        }

        return samples;
    }

    /// <summary>
    /// Runs <paramref name="input"/> through a stretcher in uneven chunks, the way a decoder hands it
    /// over, and returns everything that comes out once it is flushed.
    /// </summary>
    private static float[] stretch(float[] input, double tempo)
    {
        var stretcher = new TimeStretcher(rate, channels);
        stretcher.Reset(tempo);

        var output = new List<float>();
        float[] scratch = new float[4096 * channels];

        void drain()
        {
            int frames;

            while ((frames = stretcher.ReceiveOutput(scratch, 4096)) > 0)
                output.AddRange(scratch.AsSpan(0, frames * channels).ToArray());
        }

        int chunk = 1000;
        int start = 0;

        while (start < input.Length)
        {
            int length = Math.Min(chunk * channels, input.Length - start);
            stretcher.PutInput(input.AsSpan(start, length));
            drain();

            start += length;
            chunk = chunk == 1000 ? 3333 : 1000;
        }

        stretcher.Flush();
        drain();

        Assert.That(stretcher.Drained, Is.True);
        return output.ToArray();
    }

    /// <summary>
    /// The frequency of the left channel between two frames, from its rising zero crossings.
    /// </summary>
    private static double measureFrequency(float[] samples, int fromFrame, int toFrame)
    {
        int first = -1;
        int last = -1;
        int crossings = 0;

        for (int frame = fromFrame + 1; frame < toFrame; frame++)
        {
            float previous = samples[(frame - 1) * channels];
            float current = samples[frame * channels];

            if (previous < 0 && current >= 0)
            {
                if (first < 0)
                    first = frame;

                last = frame;
                crossings++;
            }
        }

        return (crossings - 1) / ((last - first) / (double)rate);
    }

    [TestCase(0.5)]
    [TestCase(0.75)]
    [TestCase(1.5)]
    [TestCase(2.0)]
    public void OutputLengthIsTheInputsDividedByTheTempo(double tempo)
    {
        const int frames = rate * 3;

        float[] output = stretch(sine(440, frames), tempo);

        Assert.That(output.Length / channels, Is.EqualTo(frames / tempo).Within(1),
            "A caller turns frames played into a position as frames × tempo, which is only right if the whole output is that long.");
    }

    [TestCase(0.5)]
    [TestCase(0.75)]
    [TestCase(1.5)]
    [TestCase(2.0)]
    public void PitchIsPreserved(double tempo)
    {
        const double frequency = 440;

        float[] output = stretch(sine(frequency, rate * 3), tempo);
        int outputFrames = output.Length / channels;

        // The middle of the output, clear of the fade-in and the padded tail.
        double measured = measureFrequency(output, outputFrames / 4, outputFrames * 3 / 4);

        Assert.That(measured, Is.EqualTo(frequency).Within(2).Percent,
            $"At tempo {tempo} a {frequency} Hz tone came out at {measured:F1} Hz; resampling would have given {frequency * tempo:F1} Hz.");
    }

    [Test]
    public void UnityTempoPassesTheAudioThroughUnchanged()
    {
        float[] input = sine(440, rate);
        float[] output = stretch(input, 1);

        Assert.That(output.Length, Is.EqualTo(input.Length));

        // Everything past the first crossfade, which fades in from silence like any fresh start.
        int skip = rate / 100 * channels;

        for (int i = skip; i < input.Length; i++)
        {
            if (Math.Abs(output[i] - input[i]) > 1e-5f)
                Assert.Fail($"Sample {i} differs: {output[i]} out for {input[i]} in.");
        }
    }

    /// <summary>
    /// Output frame n carries input from around n × tempo. A tone that starts at a known point in the
    /// input has to start at that point divided by the tempo in the output, or a channel's reported
    /// position would be off from what is heard.
    /// </summary>
    [TestCase(0.5)]
    [TestCase(0.75)]
    [TestCase(1.5)]
    [TestCase(2.0)]
    public void OutputStaysAlignedWithTheInput(double tempo)
    {
        const int onset = rate; // 1 s of silence, then a tone

        float[] input = new float[rate * 3 * channels];
        Array.Copy(sine(440, rate * 2), 0, input, onset * channels, rate * 2 * channels);

        float[] output = stretch(input, tempo);

        int heard = -1;

        for (int frame = 0; frame < output.Length / channels; frame++)
        {
            if (Math.Abs(output[frame * channels]) > 0.05f)
            {
                heard = frame;
                break;
            }
        }

        double heardMs = heard / (double)rate * 1000;
        double expectedMs = onset / tempo / rate * 1000;

        // Within a sequence the audio plays at its own speed, so an onset can be heard up to half a
        // sequence × |1 − tempo| of input from where the arithmetic puts it (the stretcher centres that
        // drift), plus the search range and the crossfade. The longest sequence is 125 ms.
        double toleranceMs = (125 / 2.0 * Math.Abs(1 - tempo) + 25 + 8) / tempo;

        Assert.That(heardMs, Is.EqualTo(expectedMs).Within(toleranceMs),
            $"The tone starts {onset / (double)rate * 1000:F0} ms into the input, so at tempo {tempo} it should be heard at {expectedMs:F0} ms.");
    }

    [Test]
    public void ResetDiscardsEverythingBuffered()
    {
        var stretcher = new TimeStretcher(rate, channels);
        stretcher.Reset(0.5);
        stretcher.PutInput(sine(440, rate));

        Assert.That(stretcher.AvailableFrames, Is.GreaterThan(0));

        stretcher.Reset(2);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stretcher.AvailableFrames, Is.Zero);
            Assert.That(stretcher.Drained, Is.False);
            Assert.That(stretcher.Tempo, Is.EqualTo(2));
        }
    }

    #region TempoPcmSource

    private static TempoPcmSource tempoSource(int frames, out MemoryPcmSource inner)
    {
        inner = new MemoryPcmSource(PcmBuffer.FromSamples(sine(440, frames), channels, rate));
        return new TempoPcmSource(inner, rate, channels);
    }

    private static int readAll(TempoPcmSource source, int frames)
    {
        float[] scratch = new float[512 * channels];
        int total = 0;

        while (total < frames)
        {
            int read = source.ReadFrames(scratch, Math.Min(512, frames - total));

            if (read == 0)
                break;

            total += read;
        }

        return total;
    }

    [Test]
    public void AtUnityTheSourceIsPassedStraightThrough()
    {
        using var source = tempoSource(rate, out var inner);

        float[] direct = new float[1000 * channels];
        new MemoryPcmSource(PcmBuffer.FromSamples(sine(440, rate), channels, rate)).ReadFrames(direct, 1000);

        float[] wrapped = new float[1000 * channels];
        source.ReadFrames(wrapped, 1000);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(wrapped, Is.EqualTo(direct));
            Assert.That(source.PositionMs, Is.EqualTo(inner.PositionMs));
        }
    }

    [Test]
    public void PositionAdvancesByTheTempo()
    {
        using var source = tempoSource(rate * 4, out _);

        source.SetTempo(0.5);
        readAll(source, rate);

        Assert.That(source.PositionMs, Is.EqualTo(500).Within(1),
            "A second of output at half speed is half a second of the song.");
    }

    [Test]
    public void LeavingUnityStartsWhereTheSourceIs()
    {
        using var source = tempoSource(rate * 4, out var inner);

        readAll(source, rate / 2);

        bool moved = source.SetTempo(2);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(moved, Is.False, "Nothing was read ahead at unity, so there is nothing to move back over.");
            Assert.That(source.PositionMs, Is.EqualTo(500).Within(1));
            Assert.That(inner.PositionMs, Is.EqualTo(500).Within(1));
        }

        readAll(source, rate / 2);

        Assert.That(source.PositionMs, Is.EqualTo(1500).Within(1));
    }

    [Test]
    public void ChangingTempoWhileStretchingMovesTheSourceBackToTheListener()
    {
        using var source = tempoSource(rate * 4, out var inner);

        source.SetTempo(2);
        readAll(source, rate / 4);

        double before = source.PositionMs;

        Assert.That(inner.PositionMs, Is.GreaterThan(before),
            "The stretcher reads ahead of what it has played; that read-ahead is what a change has to undo.");

        bool moved = source.SetTempo(0.5);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(moved, Is.True);
            Assert.That(source.PositionMs, Is.EqualTo(before).Within(0.001));
            Assert.That(inner.PositionMs, Is.EqualTo(before).Within(1000.0 / rate));
        }
    }

    [Test]
    public void BackToUnityIsExactAgain()
    {
        using var source = tempoSource(rate * 4, out var inner);

        source.SetTempo(1.5);
        readAll(source, rate / 4);

        double before = source.PositionMs;
        source.SetTempo(1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(source.Tempo, Is.EqualTo(1));
            Assert.That(source.PositionMs, Is.EqualTo(before).Within(1000.0 / rate));
            Assert.That(source.PositionMs, Is.EqualTo(inner.PositionMs));
        }
    }

    [Test]
    public void SeekKeepsTheTempo()
    {
        using var source = tempoSource(rate * 4, out _);

        source.SetTempo(2);
        source.Seek(1000);
        readAll(source, rate / 2);

        Assert.That(source.PositionMs, Is.EqualTo(2000).Within(1));
    }

    [Test]
    public void EndsOnceTheStretchedTailHasPlayed()
    {
        using var source = tempoSource(rate, out _);

        source.SetTempo(2);
        int played = readAll(source, rate * 2);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(played, Is.EqualTo(rate / 2).Within(1), "A second of audio at double speed plays for half a second.");
            Assert.That(source.Ended, Is.True);
            Assert.That(source.PositionMs, Is.EqualTo(1000).Within(1));
        }
    }

    #endregion
}
