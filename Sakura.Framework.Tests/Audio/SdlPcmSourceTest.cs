// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Sakura.Framework.Audio.SdlEngine;
using Sakura.Framework.Utilities;

namespace Sakura.Framework.Tests.Audio;

/// <summary>
/// Coverage for the SDL backend's decode-to-device-format layer: the ring buffer, the SDL-backed
/// converter, and the two <see cref="IPcmSource"/> implementations.
/// </summary>
[TestFixture]
public class SdlPcmSourceTest
{
    private const int device_rate = 44100;
    private const int device_channels = 2;

    private static Stream open(string resource) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream($"Sakura.Framework.Tests.Resources.{resource}")
        ?? throw new InvalidOperationException($"Missing embedded test resource '{resource}'.");

    #region Ring buffer

    [Test]
    public void Ring_WritesAndReadsBackInOrder()
    {
        var ring = new AudioRingBuffer(8);
        float[] source = new float[] { 1, 2, 3, 4, 5 };

        Assert.That(ring.Write(source), Is.EqualTo(5));
        Assert.That(ring.Available, Is.EqualTo(5));

        float[] destination = new float[5];
        Assert.That(ring.Read(destination), Is.EqualTo(5));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(destination, Is.EqualTo(source));
            Assert.That(ring.Available, Is.Zero);
        }
    }

    [Test]
    public void Ring_WrapsAroundWithoutReordering()
    {
        var ring = new AudioRingBuffer(8);

        // Push the cursor most of the way round so the next write straddles the wrap point.
        ring.Write(new float[] { 9, 9, 9, 9, 9, 9 });
        ring.Read(new float[6]);

        float[] source = new float[] { 1, 2, 3, 4, 5 };
        Assert.That(ring.Write(source), Is.EqualTo(5));

        float[] destination = new float[5];
        Assert.That(ring.Read(destination), Is.EqualTo(5));
        Assert.That(destination, Is.EqualTo(source));
    }

    [Test]
    public void Ring_ShortWriteWhenFullRatherThanOverwriting()
    {
        var ring = new AudioRingBuffer(4);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ring.Write(new float[] { 1, 2, 3, 4, 5, 6 }), Is.EqualTo(4), "Should report what it accepted.");
            Assert.That(ring.FreeSpace, Is.Zero);
            Assert.That(ring.Write(new float[] { 7 }), Is.Zero);
        }

        float[] destination = new float[4];
        ring.Read(destination);
        Assert.That(destination, Is.EqualTo(new float[] { 1, 2, 3, 4 }), "A full buffer must not clobber unread data.");
    }

    [Test]
    public void Ring_ShortReadWhenEmpty()
    {
        var ring = new AudioRingBuffer(8);
        ring.Write(new float[] { 1, 2 });

        float[] destination = new float[5];
        Assert.That(ring.Read(destination), Is.EqualTo(2));
    }

    [Test]
    public void Ring_SurvivesConcurrentWriterAndReader()
    {
        var ring = new AudioRingBuffer(1024);
        const int total = 200_000;

        long produced = 0;
        long consumed = 0;
        float expectedNext = 0;
        bool ordered = true;

        var writer = new Thread(() =>
        {
            float[] block = new float[64];
            float next = 0;

            while (produced < total)
            {
                for (int i = 0; i < block.Length; i++)
                    block[i] = next + i;

                int written = ring.Write(block);
                next += written;
                produced += written;

                if (written < block.Length)
                    Thread.Yield();
            }
        });

        var reader = new Thread(() =>
        {
            float[] block = new float[64];

            while (consumed < total)
            {
                int read = ring.Read(block);

                for (int i = 0; i < read; i++)
                {
                    if (!Precision.AlmostEquals(block[i], expectedNext))
                        ordered = false;

                    expectedNext++;
                }

                consumed += read;

                if (read == 0)
                    Thread.Yield();
            }
        });

        writer.Start();
        reader.Start();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(writer.Join(TimeSpan.FromSeconds(20)), Is.True, "Writer did not finish.");
            Assert.That(reader.Join(TimeSpan.FromSeconds(20)), Is.True, "Reader did not finish.");
        }

        Assert.That(ordered, Is.True, "Data crossed the ring out of order or was duplicated.");
        Assert.That(consumed, Is.EqualTo(total));
    }

    #endregion

    #region Converter

    [Test]
    public void Converter_ResamplesToTheTargetRate()
    {
        using var converter = new SdlAudioConverter(22050, 2, 44100, 2);

        // One second of stereo at the source rate should come back as roughly one second at the
        // target rate, i.e. twice as many frames.
        float[] input = new float[22050 * 2];
        converter.Put(input);
        converter.Flush();

        int total = 0;
        float[] scratch = new float[8192];
        int got;

        while ((got = converter.Get(scratch)) > 0)
            total += got;

        Assert.That(total / 2, Is.EqualTo(44100).Within(2).Percent);
    }

    [Test]
    public void Converter_UpmixesMonoToStereo()
    {
        using var converter = new SdlAudioConverter(44100, 1, 44100, 2);

        float[] input = new float[1000];
        Array.Fill(input, 0.5f);
        converter.Put(input);
        converter.Flush();

        float[] output = new float[4096];
        int got = converter.Get(output);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(got, Is.EqualTo(2000).Within(2).Percent, "Mono in should be stereo out, same frame count.");
            Assert.That(output[0], Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(output[1], Is.EqualTo(0.5f).Within(0.001f));
        }
    }

    [Test]
    public void Converter_ClearDiscardsPendingAudio()
    {
        using var converter = new SdlAudioConverter(44100, 2, 44100, 2);

        converter.Put(new float[4096]);
        converter.Clear();

        Assert.That(converter.Available, Is.Zero);
    }

    #endregion

    #region Memory source

    [Test]
    public void Memory_DecodesToDeviceFormatAndPlaysThrough()
    {
        var buffer = PcmBuffer.Decode(open("Samples.test.wav"), device_rate, device_channels);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(buffer.SampleRate, Is.EqualTo(device_rate));
            Assert.That(buffer.Channels, Is.EqualTo(device_channels));
            Assert.That(buffer.LengthMs, Is.EqualTo(424.6).Within(5).Percent);
        }

        using var source = new MemoryPcmSource(buffer);

        float[] destination = new float[512 * device_channels];
        long frames = 0;
        int read;

        while ((read = source.ReadFrames(destination, 512)) > 0)
            frames += read;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(source.Ended, Is.True);
            Assert.That(frames, Is.EqualTo(buffer.FrameCount));
            Assert.That(source.PositionMs, Is.EqualTo(buffer.LengthMs).Within(0.001));
        }
    }

    [Test]
    public void Memory_SeekMovesThePositionAndClampsToTheEnds()
    {
        var buffer = PcmBuffer.Decode(open("Samples.test.wav"), device_rate, device_channels);
        using var source = new MemoryPcmSource(buffer);

        source.Seek(200);
        Assert.That(source.PositionMs, Is.EqualTo(200).Within(1));

        source.Seek(-500);
        Assert.That(source.PositionMs, Is.Zero);

        source.Seek(buffer.LengthMs * 2);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(source.PositionMs, Is.EqualTo(buffer.LengthMs).Within(0.001));
            Assert.That(source.Ended, Is.True);
        }
    }

    [Test]
    public void Memory_SourcesOverOneBufferAreIndependent()
    {
        var buffer = PcmBuffer.Decode(open("Samples.test.wav"), device_rate, device_channels);

        using var first = new MemoryPcmSource(buffer);
        using var second = new MemoryPcmSource(buffer);

        first.ReadFrames(new float[256 * device_channels], 256);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.PositionMs, Is.GreaterThan(0));
            Assert.That(second.PositionMs, Is.Zero, "Cursors must not be shared between channels.");
        }
    }

    [Test]
    public void Memory_DisposeNotifiesTheOwner()
    {
        var buffer = PcmBuffer.Decode(open("Samples.test.wav"), device_rate, device_channels);

        bool released = false;
        var source = new MemoryPcmSource(buffer, () => released = true);

        source.Dispose();
        source.Dispose();

        Assert.That(released, Is.True);
    }

    #endregion

    #region Streaming source

    /// <summary>
    /// Pumps on the calling thread rather than involving the scheduler, so the test is deterministic.
    /// </summary>
    private static void pumpUntilBuffered(StreamingPcmSource source, int maxPumps = 500)
    {
        for (int i = 0; i < maxPumps && source.WantsDecode; i++)
        {
            if (!source.PumpDecode())
                break;
        }
    }

    [Test]
    public void Streaming_ReportsLengthAndDecodesAudio()
    {
        using var source = new StreamingPcmSource(open("Samples.long.mp3"), device_rate, device_channels);

        Assert.That(source.LengthMs, Is.EqualTo(4500).Within(5).Percent);

        pumpUntilBuffered(source);

        float[] destination = new float[512 * device_channels];
        int read = source.ReadFrames(destination, 512);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(read, Is.EqualTo(512));
            Assert.That(source.PositionMs, Is.EqualTo(512 / (double)device_rate * 1000.0).Within(0.001));
        }
    }

    [Test]
    public void Streaming_PlaysThroughToTheEndWithTheRightAmountOfAudio()
    {
        using var source = new StreamingPcmSource(open("Samples.long.mp3"), device_rate, device_channels);

        float[] destination = new float[512 * device_channels];
        long frames = 0;

        var timeout = Stopwatch.StartNew();

        while (!source.Ended && timeout.Elapsed < TimeSpan.FromSeconds(30))
        {
            pumpUntilBuffered(source, 8);
            frames += source.ReadFrames(destination, 512);
        }

        double decodedMs = frames / (double)device_rate * 1000.0;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(source.Ended, Is.True, "Source never reached the end.");
            Assert.That(decodedMs, Is.EqualTo(source.LengthMs).Within(2).Percent);
        }
    }

    [Test]
    public void Streaming_SeekReportsTheNewPositionImmediately()
    {
        using var source = new StreamingPcmSource(open("Samples.long.mp3"), device_rate, device_channels);

        pumpUntilBuffered(source);
        source.ReadFrames(new float[512 * device_channels], 512);

        // The decode happens on another thread in production, so the position has to move on the
        // caller's thread or a read-after-write sees a stale value.
        source.Seek(2000);
        Assert.That(source.PositionMs, Is.EqualTo(2000).Within(0.001));

        pumpUntilBuffered(source);
        int read = source.ReadFrames(new float[512 * device_channels], 512);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(read, Is.GreaterThan(0), "No audio after seeking.");
            Assert.That(source.PositionMs, Is.GreaterThan(2000));
        }
    }

    [Test]
    public void Streaming_SeekBackToTheStartAfterEndingResumesPlayback()
    {
        using var source = new StreamingPcmSource(open("Samples.long.mp3"), device_rate, device_channels);

        float[] destination = new float[4096 * device_channels];
        var timeout = Stopwatch.StartNew();

        while (!source.Ended && timeout.Elapsed < TimeSpan.FromSeconds(30))
        {
            pumpUntilBuffered(source, 8);
            source.ReadFrames(destination, 4096);
        }

        Assert.That(source.Ended, Is.True);

        // This is what looping does every time a track wraps.
        source.Seek(0);
        Assert.That(source.Ended, Is.False, "Seek must clear the ended state.");

        pumpUntilBuffered(source);

        Assert.That(source.ReadFrames(destination, 512), Is.GreaterThan(0));
    }

    [Test]
    public void Streaming_ReadingAheadOfTheDecoderCountsAnUnderrunRatherThanEnding()
    {
        using var source = new StreamingPcmSource(open("Samples.long.mp3"), device_rate, device_channels);

        // Nothing has been decoded yet, so this read cannot be satisfied.
        int read = source.ReadFrames(new float[512 * device_channels], 512);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(read, Is.Zero);
            Assert.That(source.Underruns, Is.EqualTo(1));
            Assert.That(source.Ended, Is.False, "Starving the mixer is not the end of the source.");
        }
    }

    [Test]
    public void Streaming_SchedulerKeepsTheBufferFed()
    {
        using var scheduler = new AudioDecodeScheduler();
        using var source = new StreamingPcmSource(open("Samples.long.mp3"), device_rate, device_channels);

        scheduler.Register(source);

        float[] destination = new float[512 * device_channels];
        long frames = 0;
        var timeout = Stopwatch.StartNew();

        // Drain at roughly real time and let the decode thread keep up on its own.
        while (frames < device_rate * 2 && timeout.Elapsed < TimeSpan.FromSeconds(30))
        {
            int read = source.ReadFrames(destination, 512);

            if (read == 0)
            {
                Thread.Sleep(1);
                continue;
            }

            frames += read;
            Thread.Sleep(5);
        }

        scheduler.Unregister(source);

        Assert.That(frames, Is.GreaterThanOrEqualTo(device_rate * 2), "Decode thread failed to keep two seconds of audio flowing.");
    }

    [Test]
    public void Streaming_DisposeIsSafeWhileTheSchedulerIsRunning()
    {
        var scheduler = new AudioDecodeScheduler();
        var source = new StreamingPcmSource(open("Tracks.test.mp3"), device_rate, device_channels);

        scheduler.Register(source);
        Thread.Sleep(20);

        Assert.DoesNotThrow(() =>
        {
            scheduler.Unregister(source);
            source.Dispose();
            scheduler.Dispose();
        });
    }

    [Test]
    public void Streaming_RejectsANonAudioSource()
    {
        Assert.Throws<InvalidDataException>(() =>
            _ = new StreamingPcmSource(new MemoryStream(new byte[4096]), device_rate, device_channels));
    }

    #endregion
}
