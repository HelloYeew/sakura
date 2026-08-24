// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using ManagedBass;
using NUnit.Framework;
using Sakura.Framework.Audio;
using Sakura.Framework.Audio.BassEngine;
using Sakura.Framework.Audio.SdlEngine;
using static SDL.SDL3;

namespace Sakura.Framework.Tests.Audio;

/// <summary>
/// Behavior test for all audio backend sakura shipped.
/// </summary>
[TestFixture]
public class AudioBackendConformanceTest
{
    /// <summary>
    /// How long to allow for a state change to be reflected, in milliseconds.
    /// </summary>
    /// <remarks>
    /// Generous, and deliberately not a tight bound on any backend's latency: every one of these
    /// engines marshals transport changes onto its own thread, and this is the budget for that
    /// round-trip plus the polling interval the tests use. A tight value here would measure scheduler
    /// luck.
    /// </remarks>
    private const int settle_ms = 400;

    /// <summary>
    /// Tolerance for a position assertion, in milliseconds.
    /// </summary>
    /// <remarks>
    /// One BASS playback buffer (100 ms) is the largest single quantum any shipped backend reports
    /// positions in, so that is the floor for a cross-backend comparison; 150 ms leaves room for the
    /// polling interval on top. Anything the suite is trying to catch — a seek to the wrong place, a
    /// loop to the wrong point, a rate applied the wrong way round — is wrong by far more than this.
    /// </remarks>
    private const double position_tolerance_ms = 150;

    public enum Backend
    {
        Bass,
        SdlNative,
        SdlManaged
    }

    private static IAudioManager create(Backend backend)
    {
        switch (backend)
        {
            case Backend.Bass:
                return new BassAudioManager(Bass.NoSoundDevice);

            case Backend.SdlNative:
            case Backend.SdlManaged:
                SDL_SetHint(SDL_HINT_AUDIO_DRIVER, "dummy");

                if (backend == Backend.SdlNative && !SakuraAudioEngine.IsAvailable)
                    Assert.Ignore("libsakura-audio is not available for this platform.");

                var manager = new SDLAudioManager(backend == Backend.SdlNative);

                Assert.That(manager.UsesNativeMixEngine, Is.EqualTo(backend == Backend.SdlNative),
                    "Asked for one SDL mix engine and got the other, which would make this a duplicate run of the "
                    + "other parameterization rather than a test of what it claims.");

                return manager;

            default:
                throw new ArgumentOutOfRangeException(nameof(backend));
        }
    }

    private static Stream open(string resource) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream($"Sakura.Framework.Tests.Resources.{resource}")
        ?? throw new InvalidOperationException($"Missing embedded test resource '{resource}'.");

    /// <summary>
    /// The BASS backend takes a path, not a stream, for tracks, so the shared tests use files.
    /// </summary>
    private static string writeTempCopy(string resource)
    {
        string path = Path.Combine(Path.GetTempPath(), $"sakura-conformance-{Path.GetRandomFileName()}.mp3");

        using var source = open(resource);
        using var destination = File.Create(path);
        source.CopyTo(destination);

        return path;
    }

    /// <summary>
    /// Pumps the manager until <paramref name="condition"/> holds, since every backend marshals its
    /// events onto a thread a test has to drive.
    /// </summary>
    private static bool waitUntil(IAudioManager manager, Func<bool> condition, int timeoutMs = 10_000)
    {
        var timeout = Stopwatch.StartNew();

        while (timeout.ElapsedMilliseconds < timeoutMs)
        {
            manager.Update(16);

            if (condition())
                return true;

            Thread.Sleep(8);
        }

        manager.Update(16);
        return condition();
    }

    private static void settle(IAudioManager manager)
    {
        var timeout = Stopwatch.StartNew();

        while (timeout.ElapsedMilliseconds < settle_ms)
        {
            manager.Update(16);
            Thread.Sleep(8);
        }
    }

    #region Length and playback

    [Test]
    public void ReportsATrackLength([Values] Backend backend)
    {
        using var manager = (IDisposable)create(backend);
        var audio = (IAudioManager)manager;

        var track = audio.CreateTrackFromFile(writeTempCopy("Tracks.test.mp3"));

        Assert.That(track.Length, Is.GreaterThan(1000),
            "Every backend has to agree roughly on how long a file is, or seeking and progress bars mean different "
            + "things per backend.");
    }

    [Test]
    public void PlayingAdvancesThePosition([Values] Backend backend)
    {
        using var manager = (IDisposable)create(backend);
        var audio = (IAudioManager)manager;

        var channel = audio.CreateTrackFromFile(writeTempCopy("Tracks.test.mp3")).GetChannel();
        channel.Play();

        Assert.That(waitUntil(audio, () => channel.CurrentTime > 300), Is.True,
            $"Position did not advance past 300 ms. Reached {channel.CurrentTime} ms.");
    }

    #endregion

    #region Transport

    [Test]
    public void SeekLandsWhereItWasSent([Values] Backend backend)
    {
        using var manager = (IDisposable)create(backend);
        var audio = (IAudioManager)manager;

        var track = audio.CreateTrackFromFile(writeTempCopy("Tracks.test.mp3"));
        var channel = track.GetChannel();

        channel.Play();
        waitUntil(audio, () => channel.CurrentTime > 200);

        channel.CurrentTime = 2000;
        settle(audio);

        Assert.That(channel.CurrentTime, Is.EqualTo(2000).Within(position_tolerance_ms + settle_ms),
            "A seek has to land where it was sent on every backend, or gameplay synced to one backend is wrong on "
            + "the other.");
    }

    [Test]
    public void SeekIsVisibleImmediately([Values] Backend backend)
    {
        using var manager = (IDisposable)create(backend);
        var audio = (IAudioManager)manager;

        var channel = audio.CreateTrackFromFile(writeTempCopy("Tracks.test.mp3")).GetChannel();

        channel.Play();
        waitUntil(audio, () => channel.CurrentTime > 200);

        channel.CurrentTime = 5000;

        // No settle: the very next read must not still be reporting the old position. A backend that
        // reports the pre-seek cursor for a frame makes TrackClock see a jump backwards and then
        // forwards, which it turns into audible desync.
        Assert.That(channel.CurrentTime, Is.EqualTo(5000).Within(position_tolerance_ms),
            $"Read {channel.CurrentTime} ms straight after seeking to 5000 ms.");
    }

    [Test]
    public void StopRewindsAndPauseDoesNot([Values] Backend backend)
    {
        using var manager = (IDisposable)create(backend);
        var audio = (IAudioManager)manager;

        var channel = audio.CreateTrackFromFile(writeTempCopy("Tracks.test.mp3")).GetChannel();

        channel.Play();
        waitUntil(audio, () => channel.CurrentTime > 400);

        channel.Pause();
        settle(audio);

        double paused = channel.CurrentTime;
        Assert.That(paused, Is.GreaterThan(0), "Pause rewound, which is Stop's job.");

        settle(audio);

        Assert.That(channel.CurrentTime, Is.EqualTo(paused).Within(position_tolerance_ms),
            "A paused channel kept moving.");

        channel.Stop();
        settle(audio);

        Assert.That(channel.CurrentTime, Is.EqualTo(0).Within(position_tolerance_ms),
            "Stop must rewind on every backend.");
    }

    [Test]
    public void ReplayingAfterStopStartsOver([Values] Backend backend)
    {
        using var manager = (IDisposable)create(backend);
        var audio = (IAudioManager)manager;

        var channel = audio.CreateTrackFromFile(writeTempCopy("Tracks.test.mp3")).GetChannel();

        channel.Play();
        waitUntil(audio, () => channel.CurrentTime > 400);

        channel.Stop();
        settle(audio);

        channel.Play();

        Assert.That(waitUntil(audio, () => channel.CurrentTime > 200), Is.True,
            "A stopped-then-replayed channel did not start producing audio again.");
    }

    #endregion

    #region Looping

    [Test]
    public void LoopingReturnsToTheRestartPoint([Values] Backend backend)
    {
        using var manager = (IDisposable)create(backend);
        var audio = (IAudioManager)manager;

        var track = audio.CreateTrackFromFile(writeTempCopy("Tracks.test.mp3"));
        var channel = track.GetChannel();

        channel.Looping = true;
        channel.RestartPoint = 1000;

        // Start close enough to the end that the wrap happens inside the test's patience.
        channel.CurrentTime = Math.Max(0, track.Length - 400);
        channel.Play();

        Assert.That(waitUntil(audio, () => channel.CurrentTime < track.Length - 1000), Is.True,
            $"The track never wrapped. Position sat at {channel.CurrentTime} ms against a length of {track.Length} ms.");

        // Where it wrapped *to* is the part that matters and the part a listen cannot check.
        Assert.That(channel.CurrentTime, Is.GreaterThanOrEqualTo(1000 - position_tolerance_ms),
            $"Looped back to {channel.CurrentTime} ms, before the RestartPoint of 1000 ms.");
    }

    [Test]
    public void ANonLoopingChannelReportsItsEnd([Values] Backend backend)
    {
        using var manager = (IDisposable)create(backend);
        var audio = (IAudioManager)manager;

        var sample = audio.CreateSample(open("Samples.test.wav"));
        var channel = sample.Play();

        Assert.That(channel, Is.Not.Null);

        bool ended = false;
        channel.OnEnd += () => ended = true;

        Assert.That(waitUntil(audio, () => ended), Is.True,
            "OnEnd never fired. Anything driving a sequence off sample completion breaks silently on this backend.");
    }

    #endregion

    #region Rate

    [TestCase(Backend.Bass, 2.0)]
    [TestCase(Backend.Bass, 0.5)]
    [TestCase(Backend.SdlNative, 2.0)]
    [TestCase(Backend.SdlNative, 0.5)]
    [TestCase(Backend.SdlManaged, 2.0)]
    [TestCase(Backend.SdlManaged, 0.5)]
    public void FrequencyScalesHowFastThePositionMoves(Backend backend, double rate)
    {
        using var manager = (IDisposable)create(backend);
        var audio = (IAudioManager)manager;

        var channel = audio.CreateTrackFromFile(writeTempCopy("Tracks.test.mp3")).GetChannel();

        channel.Frequency.Value = rate;
        channel.Play();

        waitUntil(audio, () => channel.CurrentTime > 200);

        double startPosition = channel.CurrentTime;
        var wall = Stopwatch.StartNew();

        waitUntil(audio, () => wall.ElapsedMilliseconds > 1000, 3000);

        double advanced = channel.CurrentTime - startPosition;
        double expected = wall.Elapsed.TotalMilliseconds * rate;

        // The failure worth catching is the ratio being inverted or ignored, which is a factor of two
        // or four out — hence a percentage band rather than a millisecond one.
        Assert.That(advanced, Is.EqualTo(expected).Within(30).Percent,
            $"At rate {rate} the position advanced {advanced:F0} ms over {wall.Elapsed.TotalMilliseconds:F0} ms of "
            + $"wall clock; {expected:F0} ms was expected.");
    }

    #endregion

    #region Mixing and polyphony

    [Test]
    public void ManyConcurrentSamplesAllPlay([Values] Backend backend)
    {
        using var manager = (IDisposable)create(backend);
        var audio = (IAudioManager)manager;

        const int voices = 16;

        var channels = new IAudioChannel[voices];

        for (int i = 0; i < voices; i++)
        {
            var sample = audio.CreateSample(open("Samples.long.mp3"));
            channels[i] = sample.Play()!;

            Assert.That(channels[i], Is.Not.Null, $"Voice {i} would not start.");
        }

        settle(audio);

        int running = 0;

        foreach (var channel in channels)
        {
            if (channel.IsRunning.Value)
                running++;
        }

        Assert.That(running, Is.EqualTo(voices),
            $"{running} of {voices} concurrent samples were still running. Hitsound polyphony is the thing this "
            + "measures, and a backend that silently drops voices under load loses notes.");
    }

    [Test]
    public void MixerVolumeIsIndependentOfChannelVolume([Values] Backend backend)
    {
        using var manager = (IDisposable)create(backend);
        var audio = (IAudioManager)manager;

        var channel = audio.CreateTrackFromFile(writeTempCopy("Tracks.test.mp3")).GetChannel();
        channel.Play();

        channel.Volume.Value = 0.5;
        audio.TrackVolume.Value = 0.25;

        settle(audio);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(channel.Volume.Value, Is.EqualTo(0.5).Within(1e-6),
                "Setting a mixer's volume changed a channel's own volume, so the two are the same control on this "
                + "backend and an app cannot use them separately.");
            Assert.That(audio.TrackVolume.Value, Is.EqualTo(0.25).Within(1e-6));
        }
    }

    [Test]
    public void StopAllStopsEveryChannel([Values] Backend backend)
    {
        using var manager = (IDisposable)create(backend);
        var audio = (IAudioManager)manager;

        var track = audio.CreateTrackFromFile(writeTempCopy("Tracks.test.mp3")).GetChannel();
        var sample = audio.CreateSample(open("Samples.long.mp3")).Play();

        track.Play();
        settle(audio);

        audio.StopAll();
        settle(audio);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(track.IsRunning.Value, Is.False, "A track kept playing through StopAll.");
            Assert.That(sample?.IsRunning.Value, Is.False, "A sample kept playing through StopAll.");
        }
    }

    #endregion

    #region Metering

    [Test]
    public void AmplitudesRespondToAudio([Values] Backend backend)
    {
        using var manager = (IDisposable)create(backend);
        var audio = (IAudioManager)manager;

        var channel = audio.CreateTrackFromFile(writeTempCopy("Tracks.loud.mp3")).GetChannel();
        channel.Play();

        bool sawSignal = waitUntil(audio,
            () => channel.CurrentAmplitudes.AmplitudeLeft > 0.01f || channel.CurrentAmplitudes.AmplitudeRight > 0.01f);

        Assert.That(sawSignal, Is.True,
            "Peak levels never rose above nothing while a deliberately loud track was playing, so any visualiser "
            + "bound to this backend renders a flat line.");
    }

    [Test]
    public void SpectrumHasContentWhilePlaying([Values] Backend backend)
    {
        using var manager = (IDisposable)create(backend);
        var audio = (IAudioManager)manager;

        var channel = audio.CreateTrackFromFile(writeTempCopy("Tracks.loud.mp3")).GetChannel();
        channel.Play();

        bool sawSpectrum = waitUntil(audio, () =>
        {
            var frequencies = channel.CurrentAmplitudes.FrequencyAmplitudes.Span;

            for (int i = 0; i < frequencies.Length; i++)
            {
                if (frequencies[i] > 0.001f)
                    return true;
            }

            return false;
        });

        Assert.That(sawSpectrum, Is.True,
            "The spectrum stayed empty on a loud track. AudioVisualizer would show nothing on this backend.");
    }

    #endregion
}
