// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Audio.SdlEngine;

namespace Sakura.Framework.Tests.Audio;

/// <summary>
/// Voice-level coverage: the mix maths, gain and pan, rate resampling, loop handling, and event
/// ordering. Runs against synthetic PCM and a stub context, so no device and no decoder is involved.
/// </summary>
[TestFixture]
public class SdlAudioChannelTest
{
    private const int rate = 44100;
    private const int channels = 2;

    /// <summary>
    /// Stands in for the manager. Queues actions rather than running them, so tests control exactly
    /// when audio-thread work happens.
    /// </summary>
    private sealed class StubContext : ISDLAudioContext
    {
        public int SampleRate => rate;
        public int Channels => channels;

        /// <summary>
        /// Output latency to report, so a test can pin position compensation.
        /// </summary>
        public double OutputLatencyMs { get; set; }

        private readonly Queue<Action> pending = new Queue<Action>();
        public int WakeCount;

        public void EnqueueAction(Action action) => pending.Enqueue(action);

        public void RaiseEvent(Action action) => action();

        public void WakeDecoder() => WakeCount++;

        /// <summary>Runs everything queued, including anything queued while draining.</summary>
        public void Drain()
        {
            while (pending.Count > 0)
                pending.Dequeue().Invoke();
        }
    }

    /// <summary>
    /// A source of constant-valued frames, so gain and pan changes are trivially readable in output.
    /// </summary>
    private static IPcmSource constant(float value, int frames)
    {
        float[] samples = new float[frames * channels];
        Array.Fill(samples, value);
        return new MemoryPcmSource(buffer(samples));
    }

    private static PcmBuffer buffer(float[] samples) => PcmBuffer.FromSamples(samples, channels, rate);

    private static SDLAudioChannel playing(StubContext context, IPcmSource source)
    {
        var channel = new SDLAudioChannel(context, source);
        channel.Play();
        context.Drain();
        return channel;
    }

    [Test]
    public void Fill_AddsIntoTheDestinationRatherThanOverwriting()
    {
        var context = new StubContext();
        var channel = playing(context, constant(0.25f, 512));

        float[] destination = new float[64 * channels];
        Array.Fill(destination, 0.5f);

        channel.Fill(destination);

        Assert.That(destination[0], Is.EqualTo(0.75f).Within(0.0001f),
            "A mixer sums children into one buffer, so Fill must add.");
    }

    [Test]
    public void Fill_ContributesNothingWhenNotPlaying()
    {
        var context = new StubContext();
        var channel = new SDLAudioChannel(context, constant(0.5f, 512));

        float[] destination = new float[64 * channels];
        channel.Fill(destination);

        Assert.That(destination, Is.All.Zero);
    }

    [Test]
    public void Fill_AppliesVolume()
    {
        var context = new StubContext();
        var channel = playing(context, constant(1.0f, 512));
        channel.Volume.Value = 0.25;

        float[] destination = new float[64 * channels];
        channel.Fill(destination);

        Assert.That(destination[0], Is.EqualTo(0.25f).Within(0.0001f));
    }

    [TestCase(-1.0, 1.0f, 0.0f)]
    [TestCase(0.0, 1.0f, 1.0f)]
    [TestCase(1.0, 0.0f, 1.0f)]
    [TestCase(0.5, 0.5f, 1.0f)]
    public void Fill_AppliesLinearPan(double balance, float expectedLeft, float expectedRight)
    {
        var context = new StubContext();
        var channel = playing(context, constant(1.0f, 512));
        channel.Balance.Value = balance;

        float[] destination = new float[64 * channels];
        channel.Fill(destination);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(destination[0], Is.EqualTo(expectedLeft).Within(0.0001f));
            Assert.That(destination[1], Is.EqualTo(expectedRight).Within(0.0001f));
        }
    }

    [Test]
    public void Fill_AtUnityFrequencyReproducesTheSourceExactly()
    {
        var context = new StubContext();
        var channel = playing(context, constant(0.5f, 512));

        float[] destination = new float[64 * channels];
        channel.Fill(destination);

        // Cubic Hermite at t=0 collapses to the sample itself, so unity rate must be lossless.
        Assert.That(destination, Is.All.EqualTo(0.5f).Within(0.0001f));
    }

    [Test]
    public void Frequency_ConsumesSourceFasterWhenRaised()
    {
        double consumedAt(double frequency)
        {
            var context = new StubContext();
            var source = constant(0.5f, 44100);
            var channel = playing(context, source);
            channel.Frequency.Value = frequency;

            channel.Fill(new float[1000 * channels]);
            return source.PositionMs;
        }

        double atUnity = consumedAt(1.0);
        double atDouble = consumedAt(2.0);
        double atHalf = consumedAt(0.5);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(atDouble, Is.EqualTo(atUnity * 2).Within(5).Percent, "Double rate should consume twice the source.");
            Assert.That(atHalf, Is.EqualTo(atUnity / 2).Within(5).Percent, "Half rate should consume half the source.");
        }
    }

    [Test]
    public void CurrentTime_ReportsAndMovesThePosition()
    {
        var context = new StubContext();
        var channel = playing(context, constant(0.5f, 44100));

        channel.Fill(new float[441 * channels]);
        Assert.That(channel.CurrentTime, Is.EqualTo(10).Within(1));

        channel.CurrentTime = 500;
        context.Drain();

        Assert.That(channel.CurrentTime, Is.EqualTo(500).Within(1));
    }

    [Test]
    public void CurrentTime_SubtractsWhatIsStillQueuedForTheDevice()
    {
        var context = new StubContext { OutputLatencyMs = 20 };
        var channel = playing(context, constant(0.5f, 44100));

        // 100 ms of source consumed, 20 ms of it still sitting in the device queue unheard.
        channel.Fill(new float[4410 * channels]);

        Assert.That(channel.CurrentTime, Is.EqualTo(80).Within(1),
            "CurrentTime feeds TrackClock, so it has to mean what the listener is hearing rather than what the mixer has reached.");
    }

    [Test]
    public void CurrentTime_KeepsAdvancingWhileTheQueueDrainsAfterAPause()
    {
        var context = new StubContext { OutputLatencyMs = 20 };
        var channel = playing(context, constant(0.5f, 44100));

        channel.Fill(new float[4410 * channels]);
        channel.Pause();
        context.Drain();

        double whilePaused = channel.CurrentTime;

        // The mixer has stopped producing, but the device is still playing what it was given.
        context.OutputLatencyMs = 0;

        Assert.That(channel.CurrentTime, Is.GreaterThan(whilePaused),
            "Audio already handed to the device is still heard after a pause, so the audible position is still moving.");
    }

    [Test]
    public void CurrentTime_IsUncompensatedWhereThereIsNothingQueued()
    {
        var context = new StubContext();
        var channel = playing(context, constant(0.5f, 44100));

        channel.Fill(new float[4410 * channels]);

        Assert.That(channel.CurrentTime, Is.EqualTo(100).Within(1),
            "With an empty queue the mix cursor and the audible position are the same thing.");
    }

    [Test]
    public void Stop_RewindsButPauseDoesNot()
    {
        var context = new StubContext();
        var channel = playing(context, constant(0.5f, 44100));

        channel.Fill(new float[4410 * channels]);
        double beforePause = channel.CurrentTime;

        channel.Pause();
        context.Drain();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(channel.IsRunning.Value, Is.False);
            Assert.That(channel.CurrentTime, Is.EqualTo(beforePause).Within(0.001), "Pause must hold position.");
        }

        channel.Stop();
        context.Drain();

        Assert.That(channel.CurrentTime, Is.Zero, "Stop must rewind, matching the BASS backend.");
    }

    [Test]
    public void ReachingTheEndRaisesOnEndAndStops()
    {
        var context = new StubContext();
        var channel = playing(context, constant(0.5f, 128));

        int ended = 0;
        int stopped = 0;
        channel.OnEnd += () => ended++;
        channel.OnStop += () => stopped++;

        // Ask for far more than the source holds.
        channel.Fill(new float[1024 * channels]);
        context.Drain();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ended, Is.EqualTo(1));
            Assert.That(stopped, Is.EqualTo(1));
            Assert.That(channel.IsRunning.Value, Is.False);
        }
    }

    [Test]
    public void EndIsRaisedOnceEvenAcrossRepeatedFills()
    {
        var context = new StubContext();
        var channel = playing(context, constant(0.5f, 128));

        int ended = 0;
        channel.OnEnd += () => ended++;

        for (int i = 0; i < 5; i++)
        {
            channel.Fill(new float[1024 * channels]);
            context.Drain();
        }

        Assert.That(ended, Is.EqualTo(1), "The end must not re-fire on every subsequent block.");
    }

    [Test]
    public void LoopingRestartsAtTheRestartPointAndKeepsPlaying()
    {
        var context = new StubContext();
        var source = constant(0.5f, 4410); // 100ms
        var channel = playing(context, source);

        channel.Looping = true;
        channel.RestartPoint = 50;

        int ended = 0;
        channel.OnEnd += () => ended++;

        channel.Fill(new float[8820 * channels]);
        context.Drain();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ended, Is.GreaterThanOrEqualTo(1), "Looping should still report the end of each pass.");
            Assert.That(channel.IsRunning.Value, Is.True, "A looping channel must not stop at the end.");
            Assert.That(channel.CurrentTime, Is.GreaterThanOrEqualTo(50), "Playback should have resumed from the restart point.");
        }
    }

    [Test]
    public void AutoDisposeReleasesTheSourceAtTheEnd()
    {
        var context = new StubContext();

        bool released = false;
        float[] samples = new float[128 * channels];
        Array.Fill(samples, 0.5f);
        var source = new MemoryPcmSource(buffer(samples), () => released = true);

        var channel = new SDLAudioChannel(context, source) { AutoDispose = true };
        channel.Play();
        context.Drain();

        channel.Fill(new float[1024 * channels]);
        context.Drain();

        Assert.That(released, Is.True);
    }

    [Test]
    public void PlayAfterFinishingStartsOver()
    {
        var context = new StubContext();
        var channel = playing(context, constant(0.5f, 128));

        channel.Fill(new float[1024 * channels]);
        context.Drain();
        Assert.That(channel.IsRunning.Value, Is.False);

        channel.Play();
        context.Drain();

        float[] destination = new float[64 * channels];
        channel.Fill(destination);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(channel.IsRunning.Value, Is.True);
            Assert.That(destination[0], Is.EqualTo(0.5f).Within(0.0001f), "Replaying should produce audio, not silence at the end.");
        }
    }

    [Test]
    public void AmplitudesFollowTheAudioAndClearWhenStopped()
    {
        var context = new StubContext();
        var channel = playing(context, constant(0.8f, 44100));
        channel.Balance.Value = -1; // hard left

        channel.Fill(new float[2048 * channels]);
        var amplitudes = channel.CurrentAmplitudes;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(amplitudes.AmplitudeLeft, Is.EqualTo(0.8f).Within(0.01f));
            Assert.That(amplitudes.AmplitudeRight, Is.Zero, "Panned hard left, the right channel is silent.");
        }

        channel.Stop();
        context.Drain();

        Assert.That(channel.CurrentAmplitudes.AmplitudeLeft, Is.Zero);
    }

    [Test]
    public void AttachedFilterAffectsTheOutput()
    {
        var context = new StubContext();
        var source = constant(1.0f, 44100);
        var channel = playing(context, source);

        var filter = channel.AttachLowPassFilter();
        filter.CutoffFrequency.Value = 200;

        float[] destination = new float[16 * channels];
        channel.Fill(destination);

        // A steady 1.0 fed into a low-pass starts from the filter's zeroed state, so the first
        // samples ramp rather than arriving at full level.
        Assert.That(destination[0], Is.LessThan(0.5f), "The filter is not in the signal path.");
    }

    [Test]
    public void DisposeStopsContributingAndNotifiesTheOwner()
    {
        var context = new StubContext();
        var channel = playing(context, constant(0.5f, 4410));

        bool disposedRaised = false;
        channel.Disposed += () => disposedRaised = true;

        channel.Dispose();
        context.Drain();

        float[] destination = new float[64 * channels];
        channel.Fill(destination);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(disposedRaised, Is.True);
            Assert.That(destination, Is.All.Zero);
        }

        Assert.DoesNotThrow(() =>
        {
            channel.Dispose();
            context.Drain();
        });
    }
}
