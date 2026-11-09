// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Tests.Statistics;

/// <summary>
/// The shown frames a host keeps for whoever wants to know when the screen changed. They're kept in the ring the threads'
/// timings use, which <see cref="ThreadFrameStatisticsTest"/> covers in full.
/// </summary>
[TestFixture]
public class ShownFrameStatisticsTest
{
    /// <summary>
    /// Every frame comes back with its three times, oldest first, and the cursor moves past them.
    /// </summary>
    [Test]
    public void FramesComeBackInOrderWithTheirTimes()
    {
        var statistics = new ShownFrameStatistics();
        long cursor = statistics.TotalFrames;

        for (int i = 1; i <= 3; i++)
        {
            statistics.Record(new ShownFrame(i * 100, i * 100 + 40, i * 100 + 90));
        }

        var frames = new ShownFrame[ShownFrameStatistics.CAPACITY];
        int count = statistics.Drain(frames, ref cursor, out long skipped);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(count, Is.EqualTo(3));
            Assert.That(skipped, Is.Zero);
            Assert.That(cursor, Is.EqualTo(3));

            for (int i = 0; i < 3; i++)
            {
                Assert.That(frames[i].Content, Is.EqualTo((i + 1) * 100));
                Assert.That(frames[i].GpuDone, Is.EqualTo((i + 1) * 100 + 40));
                Assert.That(frames[i].Shown, Is.EqualTo((i + 1) * 100 + 90));
            }
        }
    }

    /// <summary>
    /// The ring indexes by a mask, so a capacity that isn't a power of two is refused rather than kept wrongly.
    /// </summary>
    [Test]
    public void TheRingRefusesACapacityThatIsNotAPowerOfTwo()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new FrameRing<ShownFrame>(100));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new FrameRing<ShownFrame>(0));
            Assert.That(new FrameRing<ShownFrame>(64).Capacity, Is.EqualTo(64));
        }
    }
}
