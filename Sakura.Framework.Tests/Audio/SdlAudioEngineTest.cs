// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Sakura.Framework.Audio;
using Sakura.Framework.Audio.SdlEngine;
using Sakura.Framework.Statistic;
using static SDL.SDL3;

namespace Sakura.Framework.Tests.Audio;

/// <summary>
/// End-to-end coverage of the SDL mixer and manager.
/// </summary>
/// <remarks>
/// The manager tests open a real device through SDL's <c>dummy</c> driver, which consumes its queue
/// on a real-time clock exactly as hardware does.
/// </remarks>
[TestFixture]
public class SdlAudioEngineTest
{
    private const int rate = 44100;
    private const int channels = 2;

    private static Stream open(string resource) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream($"Sakura.Framework.Tests.Resources.{resource}")
        ?? throw new InvalidOperationException($"Missing embedded test resource '{resource}'.");

    #region Mixer

    private sealed class StubContext : ISDLAudioContext
    {
        public int SampleRate => rate;
        public int Channels => channels;
        public void EnqueueAction(Action action) => action();
        public void WakeDecoder() { }
    }

    private static SDLAudioChannel constantChannel(ISDLAudioContext context, float value, int frames = 44100)
    {
        float[] samples = new float[frames * channels];
        Array.Fill(samples, value);

        var channel = new SDLAudioChannel(context, new MemoryPcmSource(PcmBuffer.FromSamples(samples, channels, rate)));
        channel.Play();
        return channel;
    }

    private static SDLAudioMixer runningMixer(ISDLAudioContext context)
    {
        var mixer = new SDLAudioMixer(context);
        mixer.IsRunning.Value = true;
        return mixer;
    }

    [Test]
    public void Mixer_SumsItsChildren()
    {
        var context = new StubContext();
        var mixer = runningMixer(context);

        mixer.AddChannel(constantChannel(context, 0.25f));
        mixer.AddChannel(constantChannel(context, 0.5f));

        float[] destination = new float[64 * channels];
        mixer.Fill(destination);

        Assert.That(destination[0], Is.EqualTo(0.75f).Within(0.0001f));
    }

    [Test]
    public void Mixer_AppliesItsOwnVolumeToTheSum()
    {
        var context = new StubContext();
        var mixer = runningMixer(context);
        mixer.Volume.Value = 0.5;

        mixer.AddChannel(constantChannel(context, 0.4f));
        mixer.AddChannel(constantChannel(context, 0.4f));

        float[] destination = new float[64 * channels];
        mixer.Fill(destination);

        Assert.That(destination[0], Is.EqualTo(0.4f).Within(0.0001f), "Mixer volume should scale the summed result.");
    }

    [Test]
    public void Mixer_IsItselfAdditive()
    {
        var context = new StubContext();
        var mixer = runningMixer(context);
        mixer.AddChannel(constantChannel(context, 0.25f));

        float[] destination = new float[64 * channels];
        Array.Fill(destination, 0.5f);
        mixer.Fill(destination);

        Assert.That(destination[0], Is.EqualTo(0.75f).Within(0.0001f), "Two mixers must be able to share one output buffer.");
    }

    [Test]
    public void Mixer_TracksMembershipAndRunningCount()
    {
        var context = new StubContext();
        var mixer = runningMixer(context);

        var playing = constantChannel(context, 0.5f);
        var idle = new SDLAudioChannel(context, new MemoryPcmSource(PcmBuffer.FromSamples(new float[128 * channels], channels, rate)));

        mixer.AddChannel(playing);
        mixer.AddChannel(idle);
        mixer.AddChannel(playing); // duplicate

        using (Assert.EnterMultipleScope())
        {
            Assert.That(mixer.ActiveChannels.Count(), Is.EqualTo(2), "Adding the same channel twice should not duplicate it.");
            Assert.That(mixer.RunningChannelCount, Is.EqualTo(1));
        }

        mixer.RemoveChannel(playing);
        Assert.That(mixer.ActiveChannels.Count(), Is.EqualTo(1));
    }

    [Test]
    public void Mixer_ActiveChannelsIsSafeToEnumerateWhileMutating()
    {
        var context = new StubContext();
        var mixer = runningMixer(context);

        for (int i = 0; i < 8; i++)
            mixer.AddChannel(constantChannel(context, 0.1f, 128));

        // The BASS backend hands out its live list and relies on callers taking the same lock; this
        // one hands out a snapshot, so an unlocked read cannot throw.
        Assert.DoesNotThrow(() =>
        {
            foreach (var channel in mixer.ActiveChannels)
            {
                mixer.AddChannel(constantChannel(context, 0.1f, 128));
                mixer.RemoveChannel(channel);
            }
        });
    }

    [Test]
    public void Mixer_ContributesNothingWithNoChildren()
    {
        var mixer = runningMixer(new StubContext());

        float[] destination = new float[64 * channels];
        mixer.Fill(destination);

        Assert.That(destination, Is.All.Zero);
    }

    #endregion

    #region Manager (dummy device)

    /// <summary>
    /// Routes SDL at its dummy audio backend, so a device can be opened without hardware. Must be
    /// set before the audio subsystem is initialised.
    /// </summary>
    [OneTimeSetUp]
    public void UseDummyAudioDriver() => SDL_SetHint(SDL_HINT_AUDIO_DRIVER, "dummy");

    /// <summary>
    /// Opens a manager on one of the two mix engines. Every test below that is not specific to one of
    /// them runs against both.
    /// </summary>
    private static SDLAudioManager createManager(bool native)
    {
        if (native && !SakuraAudioEngine.IsAvailable)
            Assert.Ignore("libsakura-audio is not available for this platform, so the native mix engine cannot be tested here.");

        var manager = new SDLAudioManager(useNativeMixEngine: native);

        Assert.That(manager.UsesNativeMixEngine, Is.EqualTo(native),
            native ? "Expected the native mix engine, got the managed one." : "Expected the managed mix engine, got the native one.");

        return manager;
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Manager_OpensADeviceAndReportsItsFormat(bool native)
    {
        using var manager = createManager(native);

        // If the hint did not take, these tests would be silently relying on real hardware and
        // would not survive CI.
        Assert.That(SDL_GetCurrentAudioDriver(), Is.EqualTo("dummy"), "SDL did not honour the dummy audio driver hint.");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(manager.SampleRate, Is.GreaterThan(0));
            Assert.That(manager.Channels, Is.EqualTo(2));
            Assert.That(manager.TrackMixer, Is.Not.Null);
            Assert.That(manager.SampleMixer, Is.Not.Null);
        }
    }

    /// <summary>
    /// The managed mixer's own property: it pushes, so there is a queue to keep full.
    /// </summary>
    [Test]
    public void Manager_ManagedMixerKeepsTheDeviceQueueFed()
    {
        using var manager = createManager(native: false);

        // The mix thread should have pushed audio without anything playing at all — silence still
        // has to reach the device or the queue starves.
        Thread.Sleep(200);
        manager.Update(0);

        double queued = GlobalStatistics.Get<double>("Audio", "SDL Queued (ms)").Value;

        Assert.That(queued, Is.GreaterThan(0), "The mix thread is not filling the device queue.");
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Manager_PlaysASampleThroughToCompletion(bool native)
    {
        using var manager = createManager(native);

        var sample = manager.CreateSample(open("Samples.test.wav"));
        Assert.That(sample.Length, Is.EqualTo(424.6).Within(5).Percent);

        var channel = sample.Play();
        Assert.That(channel, Is.Not.Null);

        bool ended = false;
        channel.OnEnd += () => ended = true;

        var timeout = Stopwatch.StartNew();

        while (!ended && timeout.Elapsed < TimeSpan.FromSeconds(10))
        {
            manager.Update(0);
            Thread.Sleep(10);
        }

        Assert.That(ended, Is.True, "Sample never reported reaching its end.");
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Manager_PlaysAStreamingTrackAndAdvancesItsPosition(bool native)
    {
        using var manager = createManager(native);

        var track = manager.CreateTrackFromFile(writeTempCopy("Tracks.test.mp3"));
        Assert.That(track.Length, Is.GreaterThan(1000));

        var channel = track.GetChannel();
        channel.Play();

        var timeout = Stopwatch.StartNew();

        while (channel.CurrentTime < 200 && timeout.Elapsed < TimeSpan.FromSeconds(10))
        {
            manager.Update(0);
            Thread.Sleep(10);
        }

        Assert.That(channel.CurrentTime, Is.GreaterThanOrEqualTo(200), "Track position did not advance.");

        channel.Dispose();
        manager.Update(0);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Manager_PublishesItsStatistics(bool native)
    {
        using var manager = createManager(native);

        var sample = manager.CreateSample(open("Samples.long.mp3"));
        sample.Play();

        var timeout = Stopwatch.StartNew();

        while (GlobalStatistics.Get<int>("Audio", "SDL Active Voices").Value == 0 && timeout.Elapsed < TimeSpan.FromSeconds(5))
        {
            manager.Update(0);
            Thread.Sleep(10);
        }

        Assert.Multiple(() =>
        {
            Assert.That(GlobalStatistics.Get<int>("Audio", "SDL Active Voices").Value, Is.GreaterThan(0));

            if (native)
            {
                // Renamed rather than reused: there is a real device callback to time now, where the
                // managed mixer could only time a block it chose to push.
                Assert.That(GlobalStatistics.Get<long>("Audio", "SDL Callback (µs)").Value, Is.GreaterThan(0));
                Assert.That(GlobalStatistics.Get<long>("Audio", "SDL Put Failures").Value, Is.Zero);
            }
            else
            {
                Assert.That(GlobalStatistics.Get<long>("Audio", "SDL Mix Block (µs)").Value, Is.GreaterThan(0));
                Assert.That(GlobalStatistics.Get<double>("Audio", "SDL Queued (ms)").Value, Is.GreaterThan(0));
            }
        });
    }

    /// <summary>
    /// A steady multi-second play with no underrunning is the whole point of the decode-ahead design.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public void Manager_PlaysWithoutUnderrunning(bool native)
    {
        using var manager = createManager(native);

        long before = GlobalStatistics.Get<long>("Audio", "SDL Underruns").Value;

        var track = manager.CreateTrackFromFile(writeTempCopy("Tracks.test.mp3"));
        var channel = track.GetChannel();
        channel.Play();

        var timeout = Stopwatch.StartNew();

        while (timeout.Elapsed < TimeSpan.FromSeconds(3))
        {
            manager.Update(0);
            Thread.Sleep(10);
        }

        manager.Update(0);
        long after = GlobalStatistics.Get<long>("Audio", "SDL Underruns").Value;

        Assert.That(after - before, Is.Zero, "The device queue ran dry during steady playback.");

        channel.Dispose();
        manager.Update(0);
    }

    /// <summary>
    /// <see cref="AudioChannelExtensions.AddLowPassFilter"/> has to recognise this backend's channels
    /// too, or filtering silently becomes a no-op the moment the backend is switched.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public void AddLowPassFilter_AttachesToAnSdlChannel(bool native)
    {
        using var manager = createManager(native);

        var sample = manager.CreateSample(open("Samples.long.mp3"));
        var channel = sample.GetChannel();

        var filter = channel.AddLowPassFilter();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(filter, Is.Not.Null, "The extension does not know about SDL channels.");
            Assert.That(filter, Is.InstanceOf<SDLLowPassFilter>());
            Assert.That(filter!.CutoffFrequency.Value, Is.EqualTo(ILowPassFilter.DefaultCutoffFrequency));
        }

        filter!.CutoffFrequency.Value = 500;
        Assert.That(filter.CutoffFrequency.Value, Is.EqualTo(500));

        filter.Dispose();
        channel.Dispose();
        manager.Update(0);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Manager_MasterVolumeScalesBothMixers(bool native)
    {
        using var manager = createManager(native);

        manager.TrackVolume.Value = 0.5;
        manager.SampleVolume.Value = 0.25;
        manager.MasterVolume.Value = 0.5;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(manager.TrackMixer.Volume.Value, Is.EqualTo(0.25).Within(0.0001));
            Assert.That(manager.SampleMixer.Volume.Value, Is.EqualTo(0.125).Within(0.0001));
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Manager_StopAllStopsEveryChannel(bool native)
    {
        using var manager = createManager(native);

        var sample = manager.CreateSample(open("Samples.long.mp3"));
        var first = sample.Play();
        var second = sample.Play();

        manager.Update(0);

        manager.StopAll();
        manager.Update(0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.IsRunning.Value, Is.False);
            Assert.That(second.IsRunning.Value, Is.False);
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Manager_DisposeIsCleanWhileAudioIsPlaying(bool native)
    {
        var manager = createManager(native);

        var sample = manager.CreateSample(open("Samples.long.mp3"));
        sample.Play();

        var track = manager.CreateTrackFromFile(writeTempCopy("Tracks.test.mp3"));
        track.GetChannel().Play();

        Thread.Sleep(100);

        Assert.DoesNotThrow(() => manager.Dispose());
        Assert.DoesNotThrow(() => manager.Dispose());
    }

    #region Native mix engine only

    /// <summary>
    /// A seek on a streaming voice is the only place all three threads have to cooperate: this side
    /// moves the decoder, the audio thread performs the ring discard, and neither may write until the
    /// other has finished.
    /// </summary>
    [Test]
    public void NativeEngine_SeekLandsAndPlaybackResumesFromTheNewPosition()
    {
        using var manager = createManager(native: true);

        var track = manager.CreateTrackFromFile(writeTempCopy("Tracks.test.mp3"));
        var channel = track.GetChannel();
        channel.Play();

        var timeout = Stopwatch.StartNew();

        while (channel.CurrentTime < 200 && timeout.Elapsed < TimeSpan.FromSeconds(10))
        {
            manager.Update(0);
            Thread.Sleep(10);
        }

        Assert.That(channel.CurrentTime, Is.GreaterThanOrEqualTo(200));

        channel.CurrentTime = 1000;

        // Immediately, with no pumping at all. The audio thread has not applied the seek yet, and
        // reporting the old cursor against the new base here is what would read as a jump backwards.
        Assert.That(channel.CurrentTime, Is.EqualTo(1000).Within(1), "The seek was not reported until it had been applied.");

        timeout.Restart();

        while (channel.CurrentTime < 1200 && timeout.Elapsed < TimeSpan.FromSeconds(10))
        {
            manager.Update(0);
            Thread.Sleep(10);
        }

        // Past the target rather than merely at it: the ring was discarded, the decoder moved, and the
        // device is being fed from the new position.
        Assert.That(channel.CurrentTime, Is.GreaterThanOrEqualTo(1200), "Playback did not resume after the seek.");

        channel.Dispose();
        manager.Update(0);
    }

    /// <summary>
    /// A looping stream cannot wrap itself inside the engine — only this side can move a decoder — so
    /// the voice publishes the end and waits to be told where to go.
    /// </summary>
    [Test]
    public void NativeEngine_LoopingTrackWrapsAndKeepsPlaying()
    {
        using var manager = createManager(native: true);

        var track = manager.CreateTrackFromFile(writeTempCopy("Tracks.test.mp3"));
        var channel = track.GetChannel();

        // Tracks loop by default on both backends.
        Assert.That(channel.Looping, Is.True);

        int ends = 0;
        channel.OnEnd += () => ends++;

        channel.Play();
        channel.CurrentTime = Math.Max(0, track.Length - 300);

        var timeout = Stopwatch.StartNew();

        while (ends == 0 && timeout.Elapsed < TimeSpan.FromSeconds(15))
        {
            manager.Update(0);
            Thread.Sleep(10);
        }

        Assert.That(ends, Is.GreaterThan(0), "A looping track never reported reaching its end.");
        Assert.That(channel.IsRunning.Value, Is.True, "A looping track stopped at the loop point.");

        timeout.Restart();

        while (channel.CurrentTime > 1000 && timeout.Elapsed < TimeSpan.FromSeconds(10))
        {
            manager.Update(0);
            Thread.Sleep(10);
        }

        Assert.That(channel.CurrentTime, Is.LessThan(1000), "A looping track did not wrap back to its restart point.");

        channel.Dispose();
        manager.Update(0);
    }

    /// <summary>
    /// The engine holds the decoded PCM, and every voice playing it holds a reference — so evicting a
    /// sample from a store mid-hitsound must not cut the hitsound off.
    /// </summary>
    [Test]
    public void NativeEngine_SampleDisposedWhilePlayingKeepsItsVoiceAlive()
    {
        using var manager = createManager(native: true);

        var sample = manager.CreateSample(open("Samples.test.wav"));
        var channel = sample.Play();

        Assert.That(channel, Is.Not.Null);

        bool ended = false;
        channel.OnEnd += () => ended = true;

        manager.Update(0);

        // The loader is done with it; the voice is not. (ISample does not carry IDisposable — a store
        // evicting an entry is what does this in the real app.)
        (sample as IDisposable)?.Dispose();
        manager.Update(0);

        var timeout = Stopwatch.StartNew();

        while (!ended && timeout.Elapsed < TimeSpan.FromSeconds(10))
        {
            manager.Update(0);
            Thread.Sleep(10);
        }

        Assert.That(ended, Is.True, "A voice was cut off when the sample it shares was disposed.");
    }

    #endregion

    private static string writeTempCopy(string resource)
    {
        string path = Path.Combine(Path.GetTempPath(), $"sakura-sdl-audio-{Guid.NewGuid():N}{Path.GetExtension(resource)}");

        using (var input = open(resource))
        using (var output = File.Create(path))
            input.CopyTo(output);

        temporary_files.Add(path);
        return path;
    }

    private static readonly List<string> temporary_files = new List<string>();

    [OneTimeTearDown]
    public void CleanUpTemporaryFiles()
    {
        foreach (string path in temporary_files)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
        }

        temporary_files.Clear();
    }

    #endregion
}
