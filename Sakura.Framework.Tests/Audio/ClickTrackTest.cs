// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using Sakura.Framework.Audio;

namespace Sakura.Framework.Tests.Audio;

[TestFixture]
public class ClickTrackTest
{
    [Test]
    public void TestIsAMonoWavOfWholeBeats()
    {
        using var reader = new BinaryReader(ClickTrack.Create(600, 8));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Encoding.ASCII.GetString(reader.ReadBytes(4)), Is.EqualTo("RIFF"));
            Assert.That(reader.ReadInt32(), Is.EqualTo(reader.BaseStream.Length - 8));
            Assert.That(Encoding.ASCII.GetString(reader.ReadBytes(8)), Is.EqualTo("WAVEfmt "));
            Assert.That(reader.ReadInt32(), Is.EqualTo(16));
            Assert.That(reader.ReadInt16(), Is.EqualTo(1), "PCM");
            Assert.That(reader.ReadInt16(), Is.EqualTo(1), "mono");
            Assert.That(reader.ReadInt32(), Is.EqualTo(ClickTrack.SAMPLE_RATE));
            reader.ReadBytes(8);
            Assert.That(Encoding.ASCII.GetString(reader.ReadBytes(4)), Is.EqualTo("data"));
            Assert.That(reader.ReadInt32(), Is.EqualTo(8 * 28800 * sizeof(short)), "8 beats of 600 ms at 48 kHz");
        }
    }

    /// <summary>
    /// Each click is loudest on its beat's first sample, so it's heard where the beat is, and has died away before the
    /// next one.
    /// </summary>
    [Test]
    public void TestClicksStartOnTheirBeats()
    {
        short[] samples = read(ClickTrack.Create(600, 8));

        for (int beat = 0; beat < 8; beat++)
        {
            int start = beat * 28800;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Math.Abs(samples[start]), Is.GreaterThan(short.MaxValue / 2), $"beat {beat} starts loud");
                Assert.That(samples[start - 1 < 0 ? 28800 * 8 - 1 : start - 1], Is.Zero, $"silent just before beat {beat}");
            }
        }
    }

    [Test]
    public void TestAnAccentStartsEachBar()
    {
        short[] samples = read(ClickTrack.Create(600, 8, accentEvery: 4));

        // Same start, different pitch: they part within the first few samples.
        Assert.That(samples.AsSpan(4 * 28800, 50).SequenceEqual(samples.AsSpan(0, 50)), Is.True, "bars start alike");
        Assert.That(samples.AsSpan(28800, 50).SequenceEqual(samples.AsSpan(0, 50)), Is.False, "an ordinary beat differs");
    }

    [TestCase(600, 600)]
    [TestCase(500, 500)]
    [TestCase(1000 / 3.0, 16000 * 1000.0 / 48000)]
    [TestCase(600.01, 600)]
    public void TestIntervalIsKeptToWholeSamples(double asked, double kept)
    {
        Assert.That(ClickTrack.IntervalFor(asked), Is.EqualTo(kept).Within(1e-9));
    }

    private static short[] read(Stream wav)
    {
        using var reader = new BinaryReader(wav);
        reader.BaseStream.Position = 40;

        short[] samples = new short[reader.ReadInt32() / sizeof(short)];

        for (int i = 0; i < samples.Length; i++)
            samples[i] = reader.ReadInt16();

        return samples;
    }
}
