// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Sakura.Framework.Audio.SdlEngine;

namespace Sakura.Framework.Tests.Audio;

/// <summary>
/// A streaming voice in the native engine, driven by hand: the test mixes as the device callback would and decodes as
/// the decode thread would, so every frame is accounted for and nothing depends on timing.
/// </summary>
[TestFixture]
public class NativeStreamSeekTest
{
    private const int sample_rate = 48000;
    private const int channels = 2;

    private SakuraAudioEngine engine = null!;
    private readonly Context context = new Context();
    private readonly float[] output = new float[sample_rate * channels];

    /// <summary>
    /// A manager stand-in with nothing between the mix and the listener.
    /// </summary>
    private sealed class Context : ISDLAudioContext
    {
        public int SampleRate => sample_rate;
        public int Channels => channels;
        public double OutputLatencyMs => 0;
        public void EnqueueAction(Action action) => action();
        public void RaiseEvent(Action action) => action();

        public void WakeDecoder()
        {
        }
    }

    [SetUp]
    public void SetUp()
    {
        if (!SakuraAudioEngine.IsAvailable)
            Assert.Ignore("libsakura-audio is not available for this platform.");

        engine = SakuraAudioEngine.Create(sample_rate, channels, 128)!;
    }

    [TearDown]
    public void TearDown() => engine?.Dispose();

    private (SDLNativeAudioChannel channel, NativeStreamFeeder feeder) createTrack()
    {
        uint voice = engine.CreateVoice();
        Assert.That(engine.AddChild(engine.Root, voice), Is.True);

        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Sakura.Framework.Tests.Resources.Tracks.test.mp3")
                     ?? throw new InvalidOperationException("Missing embedded test resource.");

        var feeder = new NativeStreamFeeder(stream, engine, voice);
        return (new SDLNativeAudioChannel(context, engine, voice, feeder.LengthMs, feeder), feeder);
    }

    /// <summary>
    /// Mixes <paramref name="milliseconds"/> as the device would, in callbacks of 128 frames.
    /// </summary>
    private void mix(double milliseconds, NativeStreamFeeder? decodingAlongside = null)
    {
        int frames = (int)(milliseconds / 1000 * sample_rate);

        while (frames > 0)
        {
            int block = Math.Min(frames, 128);
            engine.Mix(output.AsSpan(0, block * channels));
            frames -= block;

            if (decodingAlongside != null)
                decode(decodingAlongside);
        }
    }

    /// <summary>
    /// Runs the feeder as the decode thread would, until it has nothing it can do right now.
    /// </summary>
    private static void decode(NativeStreamFeeder feeder)
    {
        for (int i = 0; i < 16 && feeder.WantsDecode; i++)
        {
            if (!feeder.PumpDecode())
                break;
        }
    }

    [Test]
    public void SeekingWhilePlayingCountsOnlyTheNewPositionsAudio()
    {
        var (channel, feeder) = createTrack();

        channel.Play();
        mix(200, feeder);

        Assert.That(channel.CurrentTime, Is.EqualTo(200).Within(5));

        channel.CurrentTime = 1000;

        // Callbacks come before the decode thread gets to the seek: the ring still holds the old position's audio, which
        // mustn't be played as, or counted towards, the new one.
        mix(100);

        Assert.That(channel.CurrentTime, Is.EqualTo(1000).Within(1), "Audio of the position left was counted from the new one");

        // The decode thread applies the seek, the next callback the discard, and then the new audio goes in.
        decode(feeder);
        mix(5);
        decode(feeder);

        mix(200, feeder);

        Assert.That(channel.CurrentTime, Is.EqualTo(1200).Within(10));
        Assert.That(channel.IsRunning.Value, Is.True);

        channel.Dispose();
    }

    [Test]
    public void PlayingAfterASeekWaitsForTheNewPositionsAudio()
    {
        var (channel, feeder) = createTrack();

        // As a gameplay clock starts a track: moved, then started at once.
        channel.CurrentTime = 1000;
        channel.Play();

        mix(100);

        Assert.That(channel.CurrentTime, Is.EqualTo(1000).Within(1), "The ring's old audio played after the seek");

        decode(feeder);
        mix(5);
        decode(feeder);
        mix(300, feeder);

        Assert.That(channel.CurrentTime, Is.EqualTo(1300).Within(10));

        channel.Dispose();
    }

    [Test]
    public void PausingWhileWaitingForAudioKeepsTheVoiceStopped()
    {
        var (channel, feeder) = createTrack();

        channel.CurrentTime = 1000;
        channel.Play();
        channel.Pause();

        decode(feeder);
        mix(5);
        decode(feeder);
        mix(200, feeder);

        Assert.That(channel.CurrentTime, Is.EqualTo(1000).Within(1), "The audio arriving started a voice that was paused meanwhile");
        Assert.That(channel.IsRunning.Value, Is.False);

        channel.Dispose();
    }

    [Test]
    public void StoppingRewindsTheDecoderToo()
    {
        var reference = createTrack();
        float[] start = render(reference, 100);
        reference.channel.Dispose();
        mix(5);

        var (channel, feeder) = createTrack();

        channel.Play();
        mix(1500, feeder);

        channel.Stop();
        decode(feeder);
        mix(5);
        decode(feeder);

        channel.Play();
        decode(feeder);
        mix(5);

        Assert.That(channel.CurrentTime, Is.LessThan(10));

        float[] replay = render((channel, feeder), 100, playing: true);

        Assert.That(correlation(start, replay), Is.GreaterThan(0.9),
            "Played again after a stop, the track carried on from where it stopped rather than from the start");

        channel.Dispose();
    }

    [Test]
    public void PlayingAnEndedTrackFromWhereItWasPutStartsThere()
    {
        var (channel, feeder) = createTrack();

        channel.CurrentTime = feeder.LengthMs - 100;
        channel.Play();

        for (int i = 0; i < 100 && channel.IsRunning.Value; i++)
        {
            mix(20, feeder);
            channel.PollEvents();
        }

        Assert.That(channel.IsRunning.Value, Is.False, "The track never ended, so this test shows nothing");

        // As yuuki starts a song over at its preview point: moved, then played at once, while it still reads as ended.
        channel.CurrentTime = 1000;
        channel.Play();

        decode(feeder);
        mix(5);
        decode(feeder);
        mix(200, feeder);

        Assert.That(channel.CurrentTime, Is.EqualTo(1200).Within(10), "Played from the start instead of where it was put");

        channel.Dispose();
    }

    [Test]
    public void DisposingTheFilterStopsTheVoiceFiltering()
    {
        var (channel, feeder) = createTrack();
        double unfiltered = brightness(render((channel, feeder), 200, at: 2000));

        var filter = channel.AttachLowPassFilter();
        filter.CutoffFrequency.Value = 100;
        double filtered = brightness(render((channel, feeder), 200, at: 2000));

        filter.Dispose();
        double released = brightness(render((channel, feeder), 200, at: 2000));

        Assert.That(filtered, Is.LessThan(unfiltered * 0.1), "The filter never took effect, so this test shows nothing");
        Assert.That(released, Is.EqualTo(unfiltered).Within(unfiltered * 0.05), "The voice kept filtering after the filter was disposed");

        channel.Dispose();
    }

    /// <summary>
    /// A change of speed is a seek to where the listener is, and the music stops until the new speed's audio is in. That
    /// has to be no more than the hand-over itself: the callback that takes the old audio away, and the one after. It
    /// used to be a frame of MP3 more (14 to 26 ms), as a decoder started on the target's own frame gave it back silent.
    /// </summary>
    [TestCase(1.0, 1.5)]
    [TestCase(1.5, 2.0)]
    [TestCase(2.0, 0.5)]
    [TestCase(0.75, 1.0)]
    public void ChangingSpeedOnlyPausesForTheHandOver(double from, double to)
    {
        var track = createTrack();
        var (channel, feeder) = track;

        channel.Tempo.Value = from;
        render(track, 500, at: 3000);

        channel.Tempo.Value = to;
        float[] after = render(track, 100, playing: true);

        int longest = 0;

        for (int i = 0, run = 0; i < after.Length; i++)
        {
            run = Math.Abs(after[i]) < 1e-3f ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }

        Assert.That(longest, Is.LessThanOrEqualTo(2 * 128 + sample_rate / 1000),
            $"The music stopped for {longest * 1000.0 / sample_rate:F1} ms at the change of speed.");

        channel.Dispose();
    }

    /// <summary>
    /// The left channel of <paramref name="milliseconds"/> of the track, from <paramref name="at"/>, or from where it is
    /// when <paramref name="playing"/>.
    /// </summary>
    private float[] render((SDLNativeAudioChannel channel, NativeStreamFeeder feeder) track, double milliseconds, double at = 0, bool playing = false)
    {
        var (channel, feeder) = track;

        if (!playing)
        {
            channel.Pause();
            channel.CurrentTime = at;
            channel.Play();
            decode(feeder);
            mix(5);
            decode(feeder);
        }

        int frames = (int)(milliseconds / 1000 * sample_rate);
        float[] left = new float[frames];

        for (int done = 0; done < frames;)
        {
            int block = Math.Min(frames - done, 128);
            engine.Mix(output.AsSpan(0, block * channels));

            for (int i = 0; i < block; i++)
                left[done + i] = output[i * channels];

            done += block;
            decode(feeder);
        }

        return left;
    }

    /// <summary>
    /// The energy of the sample-to-sample differences, which a low-pass takes nearly all of.
    /// </summary>
    private static double brightness(float[] samples)
    {
        double sum = 0;

        for (int i = 1; i < samples.Length; i++)
            sum += (samples[i] - samples[i - 1]) * (samples[i] - samples[i - 1]);

        return sum;
    }

    /// <summary>
    /// The best normalised correlation of <paramref name="b"/> against <paramref name="a"/> within a few milliseconds
    /// of lag either way, as a decoder's start can sit a little apart.
    /// </summary>
    private static double correlation(float[] a, float[] b)
    {
        const int max_lag = 480;
        double best = 0;

        for (int lag = -max_lag; lag <= max_lag; lag += 4)
        {
            double sum = 0, energyA = 0, energyB = 0;

            for (int i = max_lag; i < a.Length - max_lag; i++)
            {
                sum += a[i] * b[i + lag];
                energyA += a[i] * a[i];
                energyB += b[i + lag] * b[i + lag];
            }

            if (energyA > 0 && energyB > 0)
                best = Math.Max(best, sum / Math.Sqrt(energyA * energyB));
        }

        return best;
    }
}
