// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Sakura.Framework.Audio;

namespace Sakura.Framework.Tests.Audio;

[TestFixture]
public class WaveformTest
{
    private const int rate = 44100;

    private static Stream open(string resource) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream($"Sakura.Framework.Tests.Resources.{resource}")
        ?? throw new InvalidOperationException($"Missing embedded test resource '{resource}'.");

    /// <summary>
    /// A 16-bit stereo WAV of <paramref name="samples"/> (mono, played on both channels).
    /// </summary>
    internal static MemoryStream Wav(float[] samples)
    {
        var stream = new MemoryStream();
        var writer = new BinaryWriter(stream);

        const short channels = 2;
        int dataBytes = samples.Length * channels * sizeof(short);

        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(rate);
        writer.Write(rate * channels * sizeof(short));
        writer.Write((short)(channels * sizeof(short)));
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataBytes);

        foreach (float sample in samples)
        {
            short value = (short)Math.Clamp(sample * short.MaxValue, short.MinValue, short.MaxValue);
            writer.Write(value);
            writer.Write(value);
        }

        writer.Flush();
        stream.Position = 0;
        return stream;
    }

    internal static float[] Sine(double frequency, double seconds, double amplitude = 0.8)
    {
        float[] samples = new float[(int)(rate * seconds)];

        for (int i = 0; i < samples.Length; i++)
            samples[i] = (float)(amplitude * Math.Sin(2 * Math.PI * frequency * i / rate));

        return samples;
    }

    [Test]
    public async Task DecodesOnePointPerMillisecond()
    {
        using var waveform = new Waveform(Wav(Sine(440, 2)));

        var points = await waveform.GetPointsAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(points.Length, Is.EqualTo(2000).Within(2));
            Assert.That(await waveform.GetChannelsAsync(), Is.EqualTo(2));
        }
    }

    [Test]
    public async Task AmplitudeIsThePeakOfEachPoint()
    {
        float[] samples = new float[rate];
        Sine(440, 0.5, 0.25).CopyTo(samples, 0);
        Sine(440, 0.5, 0.75).CopyTo(samples, rate / 2);

        using var waveform = new Waveform(Wav(samples));
        var points = await waveform.GetPointsAsync();

        // Skip a little either side of the step; a point straddling it is legitimately in between.
        var quiet = points.Skip(50).Take(400).ToArray();
        var loud = points.Skip(550).Take(400).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(quiet.Average(p => p.AmplitudeLeft), Is.EqualTo(0.25).Within(0.03));
            Assert.That(loud.Average(p => p.AmplitudeLeft), Is.EqualTo(0.75).Within(0.03));
            Assert.That(loud.Average(p => p.AmplitudeRight), Is.EqualTo(0.75).Within(0.03));
        }
    }

    [TestCase(60, nameof(Waveform.Point.LowIntensity))]
    [TestCase(800, nameof(Waveform.Point.MidIntensity))]
    [TestCase(5000, nameof(Waveform.Point.HighIntensity))]
    public async Task ATonesBandDominates(double frequency, string band)
    {
        using var waveform = new Waveform(Wav(Sine(frequency, 1)));
        var points = await waveform.GetPointsAsync();
        var middle = points[points.Length / 2];

        float low = middle.LowIntensity;
        float mid = middle.MidIntensity;
        float high = middle.HighIntensity;

        float strongest = Math.Max(low, Math.Max(mid, high));

        float expected = band switch
        {
            nameof(Waveform.Point.LowIntensity) => low,
            nameof(Waveform.Point.MidIntensity) => mid,
            _ => high
        };

        Assert.That(expected, Is.EqualTo(strongest), $"A {frequency} Hz tone read low {low}, mid {mid}, high {high}.");
    }

    [Test]
    public async Task DecodesARealTrack()
    {
        using var waveform = new Waveform(open("Tracks.test.mp3"));
        var points = await waveform.GetPointsAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(points.Length, Is.GreaterThan(1000));
            Assert.That(points.Max(p => p.AmplitudeLeft), Is.GreaterThan(0.1f));
            Assert.That(points.All(p => p.AmplitudeLeft is >= 0 and <= 1), Is.True);
        }
    }

    [TestCase(10)]
    [TestCase(500)]
    [TestCase(5000)]
    public async Task ResamplesToTheRequestedCount(int count)
    {
        using var waveform = new Waveform(Wav(Sine(440, 2)));
        using var resampled = await waveform.GenerateResampledAsync(count);

        var points = await resampled.GetPointsAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(points.Length, Is.EqualTo(count));
            Assert.That(points[count / 2].AmplitudeLeft, Is.EqualTo(0.8).Within(0.05), "Smoothing a steady tone should leave its level alone.");
        }
    }

    [Test]
    public async Task NullDataIsAnEmptyWaveform()
    {
        using var waveform = new Waveform(null);

        Assert.That(await waveform.GetPointsAsync(), Is.Empty);
    }

    [Test]
    public async Task UndecodableDataIsAnEmptyWaveformRatherThanAThrow()
    {
        using var waveform = new Waveform(new MemoryStream(new byte[4096]));

        Assert.That(await waveform.GetPointsAsync(), Is.Empty);
    }
}
