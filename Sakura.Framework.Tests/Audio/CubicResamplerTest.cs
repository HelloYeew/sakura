// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Audio.SdlEngine;

namespace Sakura.Framework.Tests.Audio;

/// <summary>
/// The managed mixer's rate converter, which the native engine's resampler is a port of. Kept here so
/// the two stay behaviourally identical where the native tests pin the same thing.
/// </summary>
[TestFixture]
public class CubicResamplerTest
{
    private const int channels = 2;

    /// <summary>
    /// A streaming source the test feeds by hand, so it can starve the resampler and then recover.
    /// </summary>
    private sealed class QueueSource : IPcmSource
    {
        private readonly Queue<float> samples = new Queue<float>();

        public double LengthMs => 0;
        public double PositionMs => 0;
        public bool Ended => false;

        public void Push(ReadOnlySpan<float> interleaved)
        {
            foreach (float sample in interleaved)
                samples.Enqueue(sample);
        }

        public int ReadFrames(Span<float> destination, int frameCount)
        {
            int frames = Math.Min(frameCount, samples.Count / channels);

            for (int i = 0; i < frames * channels; i++)
                destination[i] = samples.Dequeue();

            return frames;
        }

        public void Seek(double milliseconds) => samples.Clear();

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// A source that starves long enough to drain the window, then recovers, plays its fresh audio from
    /// the first new frame. The position used to keep advancing over frames that never existed, one per
    /// starved block, and the recovery skipped that many fresh frames.
    /// </summary>
    [Test]
    public void TestRecoveryAfterStarvationSkipsNothing()
    {
        var source = new QueueSource();
        var resampler = new CubicResampler(channels);
        float[] block = new float[128 * channels];

        float[] start = new float[64 * channels];
        Array.Fill(start, 0.25f);
        source.Push(start);

        resampler.Read(source, block, 128, 1.0);

        for (int i = 0; i < 20; i++)
            resampler.Read(source, block, 128, 1.0);

        float[] fresh = new float[64 * channels];

        for (int i = 0; i < 64; i++)
        {
            fresh[i * 2] = 0.001f * (i + 1);
            fresh[i * 2 + 1] = -fresh[i * 2];
        }

        source.Push(fresh);
        Array.Clear(block);
        int produced = resampler.Read(source, block, 128, 1.0);

        int first = -1;

        for (int i = 0; i < produced; i++)
        {
            if (block[i * 2] != 0f)
            {
                first = i;
                break;
            }
        }

        Assert.That(first, Is.GreaterThanOrEqualTo(0), "The fresh audio never came through.");
        Assert.That(block[first * 2], Is.EqualTo(fresh[0]), "The first fresh frame was skipped.");
        Assert.That(block[(first + 1) * 2], Is.EqualTo(fresh[2]));
    }
}
