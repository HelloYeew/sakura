// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using Sakura.Framework.Audio;
using Sakura.Framework.Audio.SdlEngine;
using Sakura.Framework.Timing;
using static Sakura.Framework.Audio.SdlEngine.WasapiNative;

namespace Sakura.Framework.Tests.Audio;

/// <summary>
/// Our own WASAPI output: the formats and periods it asks Windows for, the layouts it hands the native loop, and how its
/// latency splits, on every platform. On Windows with an output, a silent stream played for real.
/// </summary>
[TestFixture]
public class WasapiOutputTest
{
    [Test]
    public unsafe void TheLayoutsMatchWhatWindowsAndTheLibraryDeclare()
    {
        Assert.Multiple(() =>
        {
            Assert.That(sizeof(WaveFormatEx), Is.EqualTo(18), "WAVEFORMATEX, packed");
            Assert.That(sizeof(WaveFormatExtensible), Is.EqualTo(40), "WAVEFORMATEXTENSIBLE, packed");
            Assert.That(sizeof(SakuraAudioWasapiConfig), Is.EqualTo(IntPtr.Size == 8 ? 72 : 48), "SakuraAudioWasapiConfig");
        });
    }

    [TestCase(128, 32u, 32u, 480u, ExpectedResult = 128u)]
    [TestCase(130, 48u, 48u, 480u, ExpectedResult = 144u)]
    [TestCase(100, 48u, 144u, 480u, ExpectedResult = 144u)]
    [TestCase(5000, 48u, 96u, 480u, ExpectedResult = 480u)]
    [TestCase(0, 48u, 96u, 480u, ExpectedResult = 96u)]
    public uint TheLowLatencyPeriodIsAMultipleOfTheFundamentalWithinTheRange(int requested, uint fundamental, uint minimum, uint maximum) =>
        WasapiOutput.LowLatencyPeriod(requested, fundamental, minimum, maximum);

    [TestCase(SakuraAudioNative.SAMPLE_FLOAT32)]
    [TestCase(SakuraAudioNative.SAMPLE_INT16)]
    [TestCase(SakuraAudioNative.SAMPLE_INT24)]
    [TestCase(SakuraAudioNative.SAMPLE_INT32)]
    public void AFormatMadeForTheLoopIsTheOneItWrites(int sampleFormat)
    {
        var format = WasapiOutput.MakeFormat(48000, 2, 0x3, sampleFormat);

        Assert.Multiple(() =>
        {
            Assert.That(WasapiOutput.SampleFormatOf(format), Is.EqualTo(sampleFormat));
            Assert.That(format.Format.BlockAlign, Is.EqualTo(2 * format.Format.BitsPerSample / 8));
            Assert.That(format.Format.AverageBytesPerSecond, Is.EqualTo(48000 * format.Format.BlockAlign));
            Assert.That(format.Format.ExtraSize, Is.EqualTo(EXTENSIBLE_EXTRA_SIZE));
        });
    }

    [Test]
    public void TwentyFourBitsInThirtyTwoAreWrittenAsThirtyTwo()
    {
        var format = WasapiOutput.MakeFormat(48000, 2, 0x3, SakuraAudioNative.SAMPLE_INT32);

        Assert.That(format.ValidBitsPerSample, Is.EqualTo(24));
        Assert.That(WasapiOutput.SampleFormatOf(format), Is.EqualTo(SakuraAudioNative.SAMPLE_INT32));
    }

    [Test]
    public void APlainFormatIsReadByItsTag()
    {
        var plainFloat = new WaveFormatExtensible
        {
            Format = new WaveFormatEx
            {
                FormatTag = WAVE_FORMAT_IEEE_FLOAT,
                BitsPerSample = 32
            }
        };

        var plainPcm = new WaveFormatExtensible
        {
            Format = new WaveFormatEx
            {
                FormatTag = WAVE_FORMAT_PCM,
                BitsPerSample = 16
            }
        };

        var doubles = new WaveFormatExtensible
        {
            Format = new WaveFormatEx
            {
                FormatTag = WAVE_FORMAT_IEEE_FLOAT,
                BitsPerSample = 64
            }
        };

        Assert.Multiple(() =>
        {
            Assert.That(WasapiOutput.SampleFormatOf(plainFloat), Is.EqualTo(SakuraAudioNative.SAMPLE_FLOAT32));
            Assert.That(WasapiOutput.SampleFormatOf(plainPcm), Is.EqualTo(SakuraAudioNative.SAMPLE_INT16));
            Assert.That(WasapiOutput.SampleFormatOf(doubles), Is.EqualTo(-1), "the loop writes no 64-bit floats");
        });
    }

    [Test]
    public void ItsLatencySplitsAtTheEndpointBufferAndStopsAtTheDevice()
    {
        Assert.That(SDLAudioManager.WasapiLatencyParts(deviceMs: 14, bufferMs: 4, measured: true), Is.EqualTo(new[]
        {
            new AudioLatencyPart(AudioLatencyPartKind.AppBuffer, 4, LatencySource.FromBufferSizes),
            new AudioLatencyPart(AudioLatencyPartKind.System, 10, LatencySource.Measured),
            AudioLatencyPart.NotReported(AudioLatencyPartKind.AfterDevice)
        }));

        Assert.That(SDLAudioManager.WasapiLatencyParts(deviceMs: 4, bufferMs: 4, measured: false), Is.EqualTo(new[]
        {
            new AudioLatencyPart(AudioLatencyPartKind.AppBuffer, 4, LatencySource.FromBufferSizes),
            AudioLatencyPart.NotReported(AudioLatencyPartKind.System)
        }));
    }

    [Test]
    public void ASilentStreamPlaysOnTheDefaultOutput()
    {
        if (!WasapiOutput.IsAvailable)
            Assert.Ignore("Windows only, with libsakura-audio.");

        if (WasapiOutput.FindEndpoint(string.Empty) == null)
            Assert.Ignore("This machine has no output.");

        using var engine = SakuraAudioEngine.Create(48000, 2)!;
        using var output = WasapiOutput.TryOpen(engine, string.Empty, exclusive: false, requestedFrames: 128);

        if (output == null)
            Assert.Ignore("No WASAPI stream opened: the libsakura-audio here may predate its WASAPI output (see the log).");

        output.Start();

        long before = engine.GetStats().Callbacks;
        var waited = Stopwatch.StartNew();

        while (engine.GetStats().Callbacks <= before + 2 && waited.ElapsedMilliseconds < 3000)
            Thread.Sleep(10);

        Assert.Multiple(() =>
        {
            Assert.That(engine.GetStats().Callbacks, Is.GreaterThan(before + 2), "the loop mixes at each event");
            Assert.That(output.Stopped, Is.False);
            Assert.That(output.BufferFrames, Is.GreaterThan(0));
            Assert.That(output.DeviceName, Is.Not.Empty);
            Assert.That(output.TrySampleLatency(out double latency), Is.True);
            Assert.That(latency, Is.InRange(0, 200));
        });
    }
}
