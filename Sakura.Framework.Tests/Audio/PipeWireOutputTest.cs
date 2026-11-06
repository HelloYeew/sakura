// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Sakura.Framework.Audio;
using Sakura.Framework.Audio.SdlEngine;
using Sakura.Framework.Timing;
using static Sakura.Framework.Audio.SdlEngine.PipeWireNative;
using static SDL.SDL3;

namespace Sakura.Framework.Tests.Audio;

/// <summary>
/// Our own PipeWire output: the format it hands the stream, byte for byte as libpipewire's builder writes it, the
/// version it reads, how it works out the latency, and its layouts, on every platform. On Linux with PipeWire running,
/// a silent stream played for real.
/// </summary>
[TestFixture]
public class PipeWireOutputTest
{
    [Test]
    public void TheStereoFormatIsWhatSpaFormatAudioRawBuildWrites()
    {
        uint[] expected =
        {
            160, 15, 0x40003, 3, // the object: its body's size, SPA_TYPE_Object, SPA_TYPE_OBJECT_Format, SPA_PARAM_EnumFormat
            1, 0, 4, 3, 1, 0, // mediaType: Id audio, padded
            2, 0, 4, 3, 1, 0, // mediaSubtype: Id raw
            0x10001, 0, 4, 3, 0x11b, 0, // format: Id F32_LE
            0x10003, 0, 4, 4, 48000, 0, // rate: Int
            0x10004, 0, 4, 4, 2, 0, // channels: Int
            0x10005, 0, 16, 13, 4, 3, 3, 4 // position: an Array of Ids, FL and FR
        };

        Assert.That(words(AudioFormat(48000, 2)), Is.EqualTo(expected));
    }

    [Test]
    public void TheMonoFormatPadsItsOnePosition()
    {
        uint[] format = words(AudioFormat(44100, 1));

        Assert.Multiple(() =>
        {
            Assert.That(format, Has.Length.EqualTo(42), "a 12-byte array body padded to 16");
            Assert.That(format[0], Is.EqualTo(160));
            Assert.That(format[^8..^2], Is.EqualTo(new uint[] { 0x10005, 0, 12, 13, 4, 3 }));
            Assert.That(format[^2..], Is.EqualTo(new uint[] { 2, 0 }), "MONO, then the padding");
        });
    }

    [TestCase("1.2.7", ExpectedResult = true)]
    [TestCase("0.3.49", ExpectedResult = true)]
    [TestCase("0.3.48", ExpectedResult = false)]
    [TestCase("0.3.65git", ExpectedResult = true)]
    [TestCase("1.0.0-1ubuntu1", ExpectedResult = true)]
    [TestCase("", ExpectedResult = false)]
    [TestCase("unknown", ExpectedResult = false)]
    public bool AVersionFromPwGetLibraryVersionHasPwBufferRequested(string version) => IsAtLeast(version, 0, 3, 49);

    [Test]
    public void TheLatencyIsTheGraphsDelayTheQueueAndTheCycleLessTheTimeSinceTheSnapshot()
    {
        var time = new Time
        {
            Now = 1_000_000_000,
            RateNumerator = 1,
            RateDenominator = 48000,
            Delay = 1024,
            Queued = 256,
            Buffered = 0
        };

        // 1024 graph ticks (21.3 ms), 256 queued frames (5.3 ms) and a 128-frame cycle (2.7 ms), 5 ms after the snapshot.
        Assert.That(PipeWireOutput.LatencyMilliseconds(time, 1_005_000_000, 128, 48000), Is.EqualTo(1408 / 48.0 - 5).Within(1e-9));
    }

    [Test]
    public unsafe void TheLayoutsMatchWhatLibpipewireAndTheLibraryDeclare()
    {
        Assert.Multiple(() =>
        {
            Assert.That(sizeof(Time), Is.EqualTo(64), "pw_time");
            Assert.That(sizeof(StreamEvents), Is.EqualTo(IntPtr.Size == 8 ? 96 : 48), "pw_stream_events");
            Assert.That(HOOK_SIZE, Is.EqualTo(6 * IntPtr.Size), "spa_hook");
            Assert.That(sizeof(SakuraAudioPipeWireConfig), Is.EqualTo(IntPtr.Size == 8 ? 32 : 20), "SakuraAudioPipeWireConfig");
        });
    }

    [Test]
    public void ASilentStreamPlaysOnTheDefaultOutput()
    {
        if (!PipeWireOutput.IsAvailable)
            Assert.Ignore("Linux only, with a libsakura-audio that has its PipeWire output.");

        using var engine = SakuraAudioEngine.Create(48000, 2)!;
        using var output = PipeWireOutput.TryOpen(engine, 256, "Sakura tests");

        if (output == null)
            Assert.Ignore("No PipeWire stream opened: PipeWire may not be running here (see the log).");

        output.Start();

        long before = engine.GetStats().Callbacks;
        var waited = Stopwatch.StartNew();

        while (engine.GetStats().Callbacks <= before + 2 && waited.ElapsedMilliseconds < 3000)
            Thread.Sleep(10);

        Assert.That(engine.GetStats().Callbacks, Is.GreaterThan(before + 2), "the process function mixes each cycle");
        Assert.That(output.Failed, Is.False);

        // Read over a second, at every point in the cycle. A reading lands anywhere within one: on a null sink, which has
        // no delay of its own, from 0 to a cycle, and a cycle that wakes late (without real-time priority, as in a
        // container) takes a reading a little below 0. Never a whole cycle below, and never as far off as a device.
        double cycleMs = 256 * 1000.0 / 48000;
        double lowest = double.MaxValue;
        double highest = double.MinValue;
        double sum = 0;
        int readings = 0;

        for (int i = 0; i < 20; i++)
        {
            Thread.Sleep(50);

            if (!output.TrySampleLatency(out double latency))
                continue;

            lowest = Math.Min(lowest, latency);
            highest = Math.Max(highest, latency);
            sum += latency;
            readings++;
        }

        TestContext.Out.WriteLine($"{output.Describe()}: {readings} readings from {lowest:F2} to {highest:F2} ms, {sum / readings:F2} on average");

        Assert.Multiple(() =>
        {
            Assert.That(readings, Is.GreaterThan(10));
            Assert.That(lowest, Is.GreaterThan(-cycleMs), "heard a cycle before now");
            Assert.That(sum / readings, Is.GreaterThan(0), "heard before now, on average");
            Assert.That(highest, Is.LessThan(500));
        });
    }

    [Test]
    public void TheManagerPlaysThroughItWhereSDLPlaysThroughPipeWire()
    {
        if (!PipeWireOutput.IsAvailable)
            Assert.Ignore("Linux only, with a libsakura-audio that has its PipeWire output.");

        // The engine tests point SDL at its dummy driver for the whole process.
        if (SDL_GetHint(SDL_HINT_AUDIO_DRIVER) is { Length: > 0 } driver && driver != "pipewire")
            Assert.Ignore($"SDL is set to its {driver} driver in this run.");

        using var manager = new SDLAudioManager(useNativeMixEngine: true, requestedDeviceBufferFrames: 256);

        if (SDL_GetCurrentAudioDriver() != "pipewire")
            Assert.Ignore("SDL doesn't play through PipeWire here.");

        // Twenty readings settle the measured figure; the manager takes one each 100 ms of its updates.
        for (int i = 0; i < 30; i++)
        {
            Thread.Sleep(100);
            manager.Update(100);
        }

        var parts = manager.GetOutputLatencyParts();
        TestContext.Out.WriteLine($"{manager.OutputLatencyMs:F2} ms: {string.Join(", ", parts)}");

        Assert.Multiple(() =>
        {
            Assert.That(manager.UsesNativeMixEngine, Is.True);
            Assert.That(parts.Select(p => p.Kind), Is.EqualTo(new[] { AudioLatencyPartKind.AppBuffer, AudioLatencyPartKind.System }));
            Assert.That(parts[1].Source, Is.EqualTo(LatencySource.Measured), "the stream's time measured it");
            Assert.That(parts.Sum(p => p.Milliseconds ?? 0), Is.EqualTo(manager.OutputLatencyMs).Within(1e-9));
        });
    }

    private static uint[] words(byte[] pod)
    {
        uint[] result = new uint[pod.Length / 4];

        for (int i = 0; i < result.Length; i++)
            result[i] = BinaryPrimitives.ReadUInt32LittleEndian(pod.AsSpan(i * 4));

        return result;
    }
}
