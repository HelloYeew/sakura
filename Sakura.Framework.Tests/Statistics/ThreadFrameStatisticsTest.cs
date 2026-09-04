// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Tests.Statistics;

[TestFixture]
public class ThreadFrameStatisticsTest
{
    private ThreadFrameStatistics statistics;
    private ThreadFrameSample[] destination;
    private long cursor;

    [SetUp]
    public void SetUp()
    {
        statistics = new ThreadFrameStatistics();
        destination = new ThreadFrameSample[ThreadFrameStatistics.CAPACITY];
        cursor = 0;
    }

    private void record(double busyMilliseconds, double budgetMilliseconds = 0) =>
        statistics.Record(new ThreadFrameSample
        {
            BusyMilliseconds = busyMilliseconds,
            BudgetMilliseconds = budgetMilliseconds
        });

    private int drain(out long skipped) => statistics.Drain(destination, ref cursor, out skipped);

    [Test]
    public void TestDrainReturnsNothingWhenNothingRecorded()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(drain(out long skipped), Is.Zero);
            Assert.That(skipped, Is.Zero);
        }
    }

    [Test]
    public void TestDrainReturnsFramesOldestFirst()
    {
        for (int i = 0; i < 5; i++)
            record(i);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(drain(out long skipped), Is.EqualTo(5));
            Assert.That(skipped, Is.Zero);
        }

        for (int i = 0; i < 5; i++)
            Assert.That(destination[i].BusyMilliseconds, Is.EqualTo(i));
    }

    [Test]
    public void TestDrainAdvancesCursor()
    {
        record(1);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(drain(out _), Is.EqualTo(1));

            // Nothing new since the last drain.
            Assert.That(drain(out long skipped), Is.Zero);
            Assert.That(skipped, Is.Zero);
        }

        record(2);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(drain(out _), Is.EqualTo(1));
            Assert.That(destination[0].BusyMilliseconds, Is.EqualTo(2));
        }
    }

    [Test]
    public void TestTotalFramesCountsEveryRecordedFrame()
    {
        for (int i = 0; i < ThreadFrameStatistics.CAPACITY * 2; i++)
            record(i);

        Assert.That(statistics.TotalFrames, Is.EqualTo(ThreadFrameStatistics.CAPACITY * 2));
    }

    [Test]
    public void TestConsumerFallingBehindKeepsNewestFramesAndReportsTheGap()
    {
        const int overrun = 10;

        for (int i = 0; i < ThreadFrameStatistics.CAPACITY + overrun; i++)
            record(i);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(drain(out long skipped), Is.EqualTo(ThreadFrameStatistics.CAPACITY));
            Assert.That(skipped, Is.EqualTo(overrun));

            // a stalled consumer should come back to the present
            // rather than replay history it can no longer act on.
            Assert.That(destination[0].BusyMilliseconds, Is.EqualTo(overrun));
            Assert.That(destination[ThreadFrameStatistics.CAPACITY - 1].BusyMilliseconds, Is.EqualTo(ThreadFrameStatistics.CAPACITY + overrun - 1));
        }
    }

    [Test]
    public void TestDestinationSmallerThanBacklogDropsOldestAndReportsTheGap()
    {
        for (int i = 0; i < 10; i++)
            record(i);

        var small = new ThreadFrameSample[4];
        int count = statistics.Drain(small, ref cursor, out long skipped);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(count, Is.EqualTo(4));
            Assert.That(skipped, Is.EqualTo(6));
            Assert.That(small[0].BusyMilliseconds, Is.EqualTo(6));
            Assert.That(small[3].BusyMilliseconds, Is.EqualTo(9));
        }

        // The cursor still advances past everything, so the dropped frames are not reported twice.
        record(10);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(statistics.Drain(small, ref cursor, out skipped), Is.EqualTo(1));
            Assert.That(skipped, Is.Zero);
        }
    }

    [Test]
    public void TestCursorSeededFromTotalFramesSkipsHistory()
    {
        for (int i = 0; i < 100; i++)
            record(i);

        cursor = statistics.TotalFrames;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(drain(out long skipped), Is.Zero);
            Assert.That(skipped, Is.Zero);
        }

        record(100);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(drain(out _), Is.EqualTo(1));
            Assert.That(destination[0].BusyMilliseconds, Is.EqualTo(100));
        }
    }

    [Test]
    public void TestCursorAheadOfProducerIsTreatedAsCurrent()
    {
        record(1);
        cursor = 1000;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(drain(out long skipped), Is.Zero);
            Assert.That(skipped, Is.Zero);
        }
    }

    [Test]
    public void TestMissedDeadlineIsScoredAgainstTheDeadlineNotTheBudget()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(new ThreadFrameSample { BusyMilliseconds = 9, DeadlineMilliseconds = 8 }.MissedDeadline, Is.True);
            Assert.That(new ThreadFrameSample { BusyMilliseconds = 7, DeadlineMilliseconds = 8 }.MissedDeadline, Is.False);

            // an update frame at 480 Hz overruns its 2ms slice without
            // coming anywhere near costing the 8.3ms frame the display was going to show.
            Assert.That(new ThreadFrameSample { BusyMilliseconds = 5, BudgetMilliseconds = 2, DeadlineMilliseconds = 8 }.MissedDeadline, Is.False);

            // a tight budget does not create a miss on its own
            Assert.That(new ThreadFrameSample { BusyMilliseconds = 5, BudgetMilliseconds = 2 }.MissedDeadline, Is.False);

            // nothing at stake, nothing to miss
            Assert.That(new ThreadFrameSample { BusyMilliseconds = 5, DeadlineMilliseconds = 0 }.MissedDeadline, Is.False);
        }
    }

    /// <summary>
    /// Every recorded frame is either handed to the consumer or counted as skipped, never silently
    /// lost, and what does arrive stays in order. This is the whole contract a consumer relies on to
    /// know its picture of the thread is complete.
    /// </summary>
    [Test]
    public void TestConcurrentProducerAccountsForEveryFrame()
    {
        const int total = 50_000;

        long observed = 0;
        long reportedSkipped = 0;
        double lastSeen = -1;
        bool outOfOrder = false;

        var producerDone = new ManualResetEventSlim(false);

        var producer = Task.Run(() =>
        {
            for (int i = 0; i < total; i++)
                record(i);

            producerDone.Set();
        });

        while (!producerDone.IsSet)
            consume();

        producer.Wait();

        // The producer may have finished mid-drain, so take one more pass for the tail.
        consume();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(producer.IsCompletedSuccessfully);
            Assert.That(outOfOrder, Is.False, "frames should arrive in the order they were recorded");
            Assert.That(observed + reportedSkipped, Is.EqualTo(total), "every frame should be delivered or accounted for as skipped");
        }
        return;

        void consume()
        {
            int count = statistics.Drain(destination, ref cursor, out long skipped);
            reportedSkipped += skipped;
            observed += count;

            for (int i = 0; i < count; i++)
            {
                if (destination[i].BusyMilliseconds <= lastSeen)
                    outOfOrder = true;

                lastSeen = destination[i].BusyMilliseconds;
            }
        }
    }
}
