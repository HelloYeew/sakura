// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Sakura.Framework.Audio.SdlEngine;

namespace Sakura.Framework.Tests.Audio;

/// <summary>
/// Test for the new FFmpeg build after native lib update 2026.819.0
/// </summary>
[TestFixture]
public class FFmpegAudioDecoderTest
{
    private static Stream open(string resource)
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Sakura.Framework.Tests.Resources.{resource}");
        Assert.That(stream, Is.Not.Null, $"Missing embedded test resource '{resource}'.");
        return stream!;
    }

    [TestCase("Tracks.test.mp3")]
    [TestCase("Tracks.loud.mp3")]
    [TestCase("Samples.long.mp3")]
    [TestCase("Samples.test.wav")]
    public void OpensAndReportsAUsableFormat(string resource)
    {
        using var decoder = new FFmpegAudioDecoder(open(resource));

        Assert.Multiple(() =>
        {
            Assert.That(decoder.SampleRate, Is.GreaterThan(0));
            Assert.That(decoder.Channels, Is.InRange(1, 8));
            Assert.That(decoder.Duration, Is.GreaterThan(0));
        });
    }

    [TestCase("Tracks.test.mp3")]
    [TestCase("Tracks.loud.mp3")]
    [TestCase("Samples.long.mp3")]
    [TestCase("Samples.test.wav")]
    public void DecodesWholeFileToInRangeSamples(string resource)
    {
        using var decoder = new FFmpegAudioDecoder(open(resource));

        float[] buffer = new float[4096];
        long totalFloats = 0;
        float peak = 0;
        int reads;

        for (reads = 0; reads < 100_000; reads++)
        {
            int read = decoder.Read(buffer);

            if (read == 0)
                break;

            Assert.That(read % decoder.Channels, Is.Zero, "Read returned a partial frame.");

            for (int i = 0; i < read; i++)
            {
                float sample = buffer[i];
                Assert.That(float.IsFinite(sample), Is.True, "Decoder produced a non-finite sample.");
                peak = Math.Max(peak, Math.Abs(sample));
            }

            totalFloats += read;
        }

        Assert.Multiple(() =>
        {
            Assert.That(decoder.EndOfStream, Is.True, "Decoder never reported end of stream.");
            Assert.That(totalFloats, Is.GreaterThan(0), "Decoder produced no audio at all.");

            // Real audio, not a silent buffer.
            Assert.That(peak, Is.GreaterThan(0.001f), "Decoded audio was silent.");

            // Float PCM is deliberately NOT clamped to +/-1.0: a lossy decoder reconstructing a hot
            // master overshoots, and loud.mp3 genuinely peaks at ~2.27 here. The bound is only a
            // sanity check against a misread sample format, which produces garbage orders of
            // magnitude out. Whatever mixes this has to expect samples above unity.
            Assert.That(peak, Is.LessThan(4f), "Decoded audio was far outside any plausible range.");

            // Duration is derived from the container, decoded length from the codec. Measured
            // agreement across all four assets is within 0.007%, so anything approaching a percent
            // means frames are being dropped or double-counted somewhere.
            double decodedMs = totalFloats / (double)decoder.Channels / decoder.SampleRate * 1000.0;
            Assert.That(decodedMs, Is.EqualTo(decoder.Duration).Within(0.5).Percent);
        });
    }

    [Test]
    public void SeekRewindsAndProducesAudioAgain()
    {
        using var decoder = new FFmpegAudioDecoder(open("Tracks.test.mp3"));

        float[] buffer = new float[4096];

        while (decoder.Read(buffer) > 0)
        {
        }

        Assert.That(decoder.EndOfStream, Is.True);

        decoder.Seek(0);

        Assert.That(decoder.EndOfStream, Is.False, "Seek did not clear the end-of-stream state.");
        Assert.That(decoder.Read(buffer), Is.GreaterThan(0), "Decoder produced nothing after seeking back to the start.");
    }

    /// <summary>
    /// A seek must land on the requested sample, not on the packet before it: every caller reports the
    /// requested time as its position, so audio from short of it plays behind the clock.
    /// </summary>
    /// <remarks>
    /// Measured rather than assumed: the audio after the seek is slid along a straight decode of the
    /// whole file until it matches, and the offset it matches at is where the seek really landed. The
    /// comparison window starts a few MP3 frames in, past the bit reservoir and filterbank state a cold
    /// decoder has to rebuild, after which the two decodes agree to the bit.
    /// </remarks>
    [TestCase("Tracks.test.mp3", 1000.0)]
    [TestCase("Tracks.test.mp3", 1234.5)]
    [TestCase("Tracks.test.mp3", 2017.3)]
    [TestCase("Samples.long.mp3", 777.7)]
    [TestCase("Samples.test.wav", 123.4)]
    public void SeekLandsOnTheRequestedSample(string resource, double targetMs)
    {
        float[] reference = decodeAll(resource, out int channels, out int sampleRate);

        using var decoder = new FFmpegAudioDecoder(open(resource));
        decoder.Seek(targetMs);

        const int settle_frames = 4096;
        const int window_frames = 2048;
        const int search_frames = 3000;

        float[] afterSeek = new float[(settle_frames + window_frames) * channels];
        int filled = 0;

        while (filled < afterSeek.Length)
        {
            int read = decoder.Read(afterSeek.AsSpan(filled));

            if (read == 0)
                break;

            filled += read;
        }

        Assert.That(filled, Is.EqualTo(afterSeek.Length), "Not enough audio after the seek to compare.");

        long expected = (long)Math.Round(targetMs / 1000.0 * sampleRate);
        long best = long.MinValue;
        double bestError = double.MaxValue;

        for (long candidate = expected - search_frames; candidate <= expected + search_frames; candidate++)
        {
            long start = (candidate + settle_frames) * channels;

            if (start < 0 || start + window_frames * channels > reference.Length)
                continue;

            double error = 0;

            for (int i = 0; i < window_frames * channels; i++)
            {
                double difference = afterSeek[settle_frames * channels + i] - reference[start + i];
                error += difference * difference;
            }

            if (error < bestError)
            {
                bestError = error;
                best = candidate;
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(best, Is.EqualTo(expected), $"Seek to {targetMs} ms landed {best - expected} frames off.");
            Assert.That(bestError, Is.LessThan(1e-6), "The audio after the seek does not match the file at any offset.");
        });
    }

    /// <summary>
    /// The audio right after a seek is the file's, from its first sample: no silence, nothing still settling.
    /// </summary>
    /// <remarks>
    /// An MP3 frame's data starts in the frames before it (the bit reservoir), so a decoder started on the target's own
    /// frame gave that frame back silent. Every seek in an MP3 began with up to a frame of silence, and a change of
    /// speed, a seek to where the listener is, put a gap in the music.
    /// </remarks>
    [TestCase("Tracks.test.mp3", 1000.0)]
    [TestCase("Tracks.test.mp3", 1234.5)]
    [TestCase("Tracks.test.mp3", 2017.3)]
    [TestCase("Samples.long.mp3", 777.7)]
    [TestCase("Samples.test.wav", 123.4)]
    public void SeekIsHeardFromItsFirstSample(string resource, double targetMs)
    {
        float[] reference = decodeAll(resource, out int channels, out int sampleRate);

        using var decoder = new FFmpegAudioDecoder(open(resource));
        decoder.Seek(targetMs);

        const int window_frames = 2048;

        float[] afterSeek = new float[window_frames * channels];
        int filled = 0;

        while (filled < afterSeek.Length)
        {
            int read = decoder.Read(afterSeek.AsSpan(filled));

            if (read == 0)
                break;

            filled += read;
        }

        Assert.That(filled, Is.EqualTo(afterSeek.Length), "Not enough audio after the seek to compare.");

        long start = (long)Math.Round(targetMs / 1000.0 * sampleRate) * channels;
        double error = 0;
        int silent = 0;

        for (int i = 0; i < afterSeek.Length; i++)
        {
            double difference = afterSeek[i] - reference[start + i];
            error += difference * difference;

            if (silent == i && afterSeek[i] == 0 && reference[start + i] != 0)
                silent++;
        }

        Assert.Multiple(() =>
        {
            Assert.That(silent / channels * 1000.0 / sampleRate, Is.Zero, "The seek began with silence (ms).");
            Assert.That(error, Is.LessThan(1e-6), "The audio after the seek is not the file's.");
        });
    }

    private static float[] decodeAll(string resource, out int channels, out int sampleRate)
    {
        using var decoder = new FFmpegAudioDecoder(open(resource));

        channels = decoder.Channels;
        sampleRate = decoder.SampleRate;

        float[] buffer = new float[8192];
        float[] all = new float[(int)(decoder.Duration / 1000.0 * decoder.SampleRate + decoder.SampleRate) * decoder.Channels];
        int total = 0;

        int read;

        while ((read = decoder.Read(buffer)) > 0)
        {
            if (total + read > all.Length)
                Array.Resize(ref all, Math.Max(all.Length * 2, total + read));

            buffer.AsSpan(0, read).CopyTo(all.AsSpan(total));
            total += read;
        }

        Array.Resize(ref all, total);
        return all;
    }

    [Test]
    public void RejectsANonAudioSource()
    {
        var garbage = new MemoryStream(new byte[4096]);
        Assert.Throws<InvalidDataException>(() => _ = new FFmpegAudioDecoder(garbage));
    }
}
