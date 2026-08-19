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

    [Test]
    public void RejectsANonAudioSource()
    {
        var garbage = new MemoryStream(new byte[4096]);
        Assert.Throws<InvalidDataException>(() => _ = new FFmpegAudioDecoder(garbage));
    }
}
