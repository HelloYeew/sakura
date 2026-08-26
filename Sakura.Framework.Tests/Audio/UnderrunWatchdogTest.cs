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
/// manager. Provoking real underruns on demand is not something a test can do, while the
/// decisions — ignore a burst, act on sustained starvation, act only once, know when doubling is not
/// the answer — are pure counting and timing.
/// </remarks>
[TestFixture]
public class UnderrunWatchdogTest
{
    private const double interval = UnderrunWatchdog.CHECK_INTERVAL_MS;

    private static readonly int window = UnderrunWatchdog.MINIMUM_OBSERVATIONS * 4;

    /// <summary>
    /// Runs one full window of <paramref name="misses"/> misses, <paramref name="met"/> deadlines met
    /// and <paramref name="idle"/> silent frames, and returns whatever the window decided.
    /// </summary>
    private static UnderrunWatchdog.Verdict? runWindow(UnderrunWatchdog watchdog, int misses, int met, int idle = 0)
    {
        UnderrunWatchdog.Verdict? last = null;

        for (int i = 0; i < misses + met + idle; i++)
        {
            var observation = i < misses ? UnderrunWatchdog.Observation.Missed
                : i < misses + met ? UnderrunWatchdog.Observation.Met
                : UnderrunWatchdog.Observation.Idle;

            bool isLast = i == misses + met + idle - 1;

            last = watchdog.Poll(observation, isLast ? interval : 0);
        }

        return last;
    }

    [Test]
    public void SaysNothingOnAHealthyDevice()
    {
        var watchdog = new UnderrunWatchdog();

        for (int i = 0; i < 10; i++)
            Assert.That(runWindow(watchdog, 0, window), Is.Null);
    }

    [Test]
    public void SaysNothingBeforeTheWindowHasElapsed()
    {
        var watchdog = new UnderrunWatchdog();

        for (int i = 0; i < window; i++)
        {
            Assert.That(watchdog.Poll(UnderrunWatchdog.Observation.Missed, 0), Is.Null,
                "A device missing every deadline is plainly wrong, but reporting it before a full window means "
                + "reporting a rate measured over an unknown amount of time.");
        }
    }

    [Test]
    public void IgnoresABurstBelowTheFraction()
    {
        var watchdog = new UnderrunWatchdog();

        int misses = (int)(window * UnderrunWatchdog.TRIP_FRACTION) - 1;

        Assert.That(runWindow(watchdog, misses, window - misses), Is.Null,
            "Startup, a device change and a hitch elsewhere in the app all make the callback late for a moment and "
            + "all recover on their own. Acting on those would make the warning meaningless.");
    }

    [Test]
    public void ReportsSustainedStarvation()
    {
        var watchdog = new UnderrunWatchdog();

        int misses = (int)(window * UnderrunWatchdog.TRIP_FRACTION) + 1;

        var verdict = runWindow(watchdog, misses, window - misses);

        Assert.That(verdict, Is.Not.Null);
        Assert.That(verdict!.Value.Misses, Is.EqualTo(misses));
        Assert.That(verdict.Value.Observations, Is.EqualTo(window));
        Assert.That(verdict.Value.Fraction, Is.GreaterThan(UnderrunWatchdog.TRIP_FRACTION));
    }

    [Test]
    public void ReportsOnlyOncePerRun()
    {
        var watchdog = new UnderrunWatchdog();

        Assert.That(runWindow(watchdog, window, 0), Is.Not.Null);

        for (int i = 0; i < 10; i++)
        {
            Assert.That(runWindow(watchdog, window, 0), Is.Null,
                "A device that is underrunning keeps underrunning. Reporting every window buries the log, and backing "
                + "off every window would take a machine from 128 frames to 1024 in twenty seconds on one bad patch.");
        }
    }

    [Test]
    public void MeasuresAShareRatherThanACount()
    {
        var watchdog = new UnderrunWatchdog();

        // The regression of this whole type was rewritten for. A handful of misses per window is what a
        // seek, a loop point or a track starting produces, and a session does that all day. Counted,
        // it walks the buffer to the cap; as a share of a busy window it is nothing.
        for (int i = 0; i < 50; i++)
        {
            Assert.That(runWindow(watchdog, 5, window - 5), Is.Null,
                "A few late callbacks in a window full of prompt ones is not steady crackling, and only steady "
                + "crackling means the buffer is the wrong size.");
        }
    }

    [Test]
    public void DoesNotTripOnAWindowWithTooLittleToGoOn()
    {
        var watchdog = new UnderrunWatchdog();

        Assert.That(runWindow(watchdog, UnderrunWatchdog.MINIMUM_OBSERVATIONS - 1, 0), Is.Null,
            "A window in which audio played for a handful of frames reads 100% on no evidence at all, and startup is "
            + "full of windows like that.");
    }

    [Test]
    public void IdleFramesAreNotEvidenceOfHealth()
    {
        var watchdog = new UnderrunWatchdog();

        // A device that missed every deadline it was actually asked to meet, in a session that was
        // mostly silent. Counting the silence as healthy would dilute this below the trip fraction.
        Assert.That(runWindow(watchdog, UnderrunWatchdog.MINIMUM_OBSERVATIONS, 0, idle: window), Is.Not.Null);
    }

    [Test]
    public void WindowsDoNotCarryOver()
    {
        var watchdog = new UnderrunWatchdog();

        Assert.That(runWindow(watchdog, window, 0, idle: 0), Is.Not.Null);

        var fresh = new UnderrunWatchdog();

        // A bad window followed by good ones must not leave a residue that trips a later one.
        Assert.That(runWindow(fresh, (int)(window * UnderrunWatchdog.TRIP_FRACTION) - 1, window), Is.Null);
        Assert.That(runWindow(fresh, 0, window), Is.Null);
        Assert.That(runWindow(fresh, (int)(window * UnderrunWatchdog.TRIP_FRACTION) - 1, window), Is.Null,
            "Misses carried between windows would turn a session that misbehaved once an hour ago into a session "
            + "that is misbehaving now.");
    }

    [Test]
    public void AccumulatesPartialFramesUpToAWindow()
    {
        var watchdog = new UnderrunWatchdog();

        // A realistic frame time rather than one poll per window.
        for (double elapsed = 0; elapsed < interval - 16; elapsed += 16)
            Assert.That(watchdog.Poll(UnderrunWatchdog.Observation.Missed, 16), Is.Null);

        Assert.That(watchdog.Poll(UnderrunWatchdog.Observation.Missed, 16), Is.Not.Null,
            "The window should close on accumulated frame time.");
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
