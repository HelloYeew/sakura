// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Diagnostics;
using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// Predicting the next refresh from frames shown, telling a missed refresh from a pause, and waiting after the drawable
/// for as long as the frame can still make the refresh.
/// </summary>
[TestFixture]
public class LateLatchTest
{
    private static readonly double ticks_per_ms = Stopwatch.Frequency / 1000.0;

    [TestCase(1050, ExpectedResult = 1100)]
    [TestCase(1100, ExpectedResult = 1100)]
    [TestCase(999, ExpectedResult = 1000)]
    [TestCase(1301, ExpectedResult = 1400)]
    public long TestTheNextRefreshIsOnTheShownFramesGrid(long after) => RefreshCadence.NextRefresh(1000, 100, after);

    [Test]
    public void TestAFrameTwoRefreshesLateMissedItsRefreshAndAPauseDidNot()
    {
        var cadence = new RefreshCadence();

        Assert.Multiple(() =>
        {
            Assert.That(cadence.Add(1000, 100), Is.False, "the first frame has nothing to go by");
            Assert.That(cadence.Add(1100, 100), Is.False);
            Assert.That(cadence.Add(1300, 100), Is.True, "two refreshes after the one before");
            Assert.That(cadence.Add(2300, 100), Is.False, "ten refreshes: a pause");
            Assert.That(cadence.LastShown, Is.EqualTo(2300));
        });
    }

    [TestCase(6, ExpectedResult = false)]
    [TestCase(7, ExpectedResult = true)]
    public bool TestAWindowMissingMoreThanOnePercentOfItsRefreshesSaysSo(int misses)
    {
        var cadence = new RefreshCadence();
        long shown = 1000;
        cadence.Add(shown, 100);

        for (int i = 0; i < 600; i++)
        {
            shown += i < misses ? 200 : 100;
            cadence.Add(shown, 100);
        }

        bool tooMany = cadence.TryEndWindow(out int counted);

        Assert.That(counted, Is.EqualTo(misses));
        Assert.That(cadence.TryEndWindow(out _), Is.False, "the next window has only begun");
        return tooMany;
    }

    [Test]
    public void TestTheUpdateIsTakenBeforeTheRefreshByTheDrawTheGpuAndTheSlack()
    {
        long refresh = (long)(100 * ticks_per_ms);

        Assert.That(LateLatch.Deadline(refresh, 0.3, 1.2, 1.0), Is.EqualTo(refresh - (long)(2.5 * ticks_per_ms)));
    }

    [Test]
    public void TestMissesGrowTheSlackUntilItGivesUp()
    {
        using var latch = new LateLatch(allowSpinning: false);

        // From 1 ms to the most, 4, half a millisecond a miss.
        for (int i = 0; i < 6; i++)
            latch.FrameShown(missedRefresh: true);

        Assert.That(latch.GivenUp, Is.False);

        for (int i = 0; i < 20; i++)
            latch.FrameShown(missedRefresh: true);

        Assert.That(latch.GivenUp, Is.True);
    }

    [Test]
    public void TestItDoesNotWaitBeforeAFrameHasBeenSeen()
    {
        using var latch = new LateLatch(allowSpinning: false);
        latch.Wait(new RefreshCadence(), 8.33 * ticks_per_ms, 0.3, 1, precise: false);

        Assert.That(latch.Waited, Is.False);
    }

    /// <summary>
    /// A frame shown 2 ms ago at 120 Hz: the next refresh is 6.3 ms away, so with 2.3 ms of draw, GPU and slack the update
    /// is taken about 4 ms from now. A sleep can't return early, so it's never before.
    /// </summary>
    [Test]
    public void TestItWaitsUntilShortlyBeforeTheNextRefresh()
    {
        double period = 1000.0 / 120 * ticks_per_ms;
        var cadence = new RefreshCadence();
        long start = Stopwatch.GetTimestamp();
        cadence.Add(start - (long)(2 * ticks_per_ms), period);

        using var latch = new LateLatch(allowSpinning: false);
        latch.Wait(cadence, period, 0.3, 1, precise: false);

        long deadline = LateLatch.Deadline(RefreshCadence.NextRefresh(cadence.LastShown, period, start), 0.3, 1, 1);

        Assert.Multiple(() =>
        {
            Assert.That(latch.Waited, Is.True);
            Assert.That(Stopwatch.GetTimestamp(), Is.GreaterThanOrEqualTo(deadline));
            Assert.That((Stopwatch.GetTimestamp() - start) / ticks_per_ms, Is.LessThan(100), "never a frame's worth");
        });
    }
}
