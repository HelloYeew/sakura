// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Audio.SdlEngine;

namespace Sakura.Framework.Tests.Audio;

/// <summary>
/// When a starving device means the buffer is wrong, and what to do about it.
/// </summary>
/// <remarks>
/// The policy that lets the framework ship an aggressive device buffer. It is tested apart from the
/// manager because provoking real underruns on demand is not something a test can do, while the
/// decisions — ignore a burst, act on sustained starvation, act only once, know when doubling is not
/// the answer — are pure counting and timing.
/// </remarks>
[TestFixture]
public class UnderrunWatchdogTest
{
    private const double interval = UnderrunWatchdog.CHECK_INTERVAL_MS;

    [Test]
    public void SaysNothingOnAHealthyDevice()
    {
        var watchdog = new UnderrunWatchdog();

        for (int i = 0; i < 10; i++)
            Assert.That(watchdog.Poll(0, interval), Is.Null);
    }

    [Test]
    public void SaysNothingBeforeTheWindowHasElapsed()
    {
        var watchdog = new UnderrunWatchdog();

        Assert.That(watchdog.Poll(1000, interval - 1), Is.Null,
            "A thousand underruns is plainly wrong, but reporting it before a full window means reporting a rate "
            + "measured over an unknown amount of time.");
    }

    [Test]
    public void IgnoresABurstBelowTheThreshold()
    {
        var watchdog = new UnderrunWatchdog();

        Assert.That(watchdog.Poll(UnderrunWatchdog.THRESHOLD - 1, interval), Is.Null,
            "Startup, a device change and a hitch elsewhere in the app all produce a few underruns and all recover "
            + "on their own. Acting on those would make the warning meaningless.");
    }

    [Test]
    public void ReportsSustainedStarvation()
    {
        var watchdog = new UnderrunWatchdog();

        Assert.That(watchdog.Poll(UnderrunWatchdog.THRESHOLD, interval), Is.EqualTo(UnderrunWatchdog.THRESHOLD));
    }

    [Test]
    public void ReportsOnlyOncePerRun()
    {
        var watchdog = new UnderrunWatchdog();

        Assert.That(watchdog.Poll(100, interval), Is.Not.Null);

        for (int i = 0; i < 10; i++)
        {
            Assert.That(watchdog.Poll(100 + (i + 1) * 100, interval), Is.Null,
                "A device that is underrunning keeps underrunning. Reporting every window buries the log, and backing "
                + "off every window would take a machine from 128 frames to 1024 in twenty seconds on one bad patch.");
        }
    }

    [Test]
    public void MeasuresARateRatherThanATotal()
    {
        var watchdog = new UnderrunWatchdog();

        // A long-running session that accumulated underruns slowly: well past the threshold in total,
        // never near it in any one window.
        long total = 0;

        for (int i = 0; i < 50; i++)
        {
            total += UnderrunWatchdog.THRESHOLD - 1;

            Assert.That(watchdog.Poll(total, interval), Is.Null,
                "Occasional underruns over an hour are not the same failure as steady crackling, and only the second "
                + "one means the buffer is the wrong size.");
        }
    }

    [Test]
    public void AccumulatesPartialFramesUpToAWindow()
    {
        var watchdog = new UnderrunWatchdog();

        // A realistic frame time rather than one poll per window.
        for (double elapsed = 0; elapsed < interval - 16; elapsed += 16)
            Assert.That(watchdog.Poll(100, 16), Is.Null);

        Assert.That(watchdog.Poll(100, 16), Is.Not.Null, "The window should close on accumulated frame time.");
    }

    [TestCase(128, 256)]
    [TestCase(256, 512)]
    [TestCase(512, 1024)]
    public void BacksOffByDoubling(int current, int expected)
    {
        Assert.That(UnderrunWatchdog.NextBufferSize(current), Is.EqualTo(expected));
    }

    [Test]
    public void DoesNotBackOffPastTheCap()
    {
        Assert.That(UnderrunWatchdog.NextBufferSize(UnderrunWatchdog.MAX_AUTO_BACKOFF_FRAMES), Is.Null,
            "1024 frames is roughly what SDL picks unprompted, so past it 'the buffer is too small' has stopped being "
            + "a credible explanation and doubling trades latency for nothing.");
    }

    [Test]
    public void DoesNotBackOffAboveTheCap()
    {
        Assert.That(UnderrunWatchdog.NextBufferSize(4096), Is.Null);
    }

    [Test]
    public void DoesNotBackOffWhenTheBufferWasNeverOurs()
    {
        Assert.That(UnderrunWatchdog.NextBufferSize(0), Is.Null,
            "Zero means SDL chose the buffer, so it is already the driver's own preference and there is nothing for "
            + "us to correct.");
    }

    [Test]
    public void BackoffConvergesRatherThanRunningAway()
    {
        // One doubling per launch, as the manager applies it: a machine that keeps failing walks up to
        // the cap and then stops asking.
        int size = 128;
        int launches = 0;

        while (UnderrunWatchdog.NextBufferSize(size) is { } next)
        {
            size = next;
            launches++;

            Assert.That(launches, Is.LessThan(10), "Backoff is not converging.");
        }

        Assert.That(size, Is.EqualTo(UnderrunWatchdog.MAX_AUTO_BACKOFF_FRAMES));
        Assert.That(launches, Is.EqualTo(3), "128 to 1024 should take three bad launches, not more.");
    }
}
