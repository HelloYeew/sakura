// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Audio.SdlEngine;

namespace Sakura.Framework.Tests.Audio;

/// <summary>
/// The managed mixer's starvation measurement, which has to survive the thread doing the measuring
/// being stopped for the whole event.
/// </summary>
[TestFixture]
public class DeviceStarvationTrackerTest
{
    [Test]
    public void CountsNothingWhenTheQueueOutlastsTheGap()
    {
        var tracker = new DeviceStarvationTracker();

        tracker.Observe(gapMs: 5, playableMs: 80, anythingPlaying: true);

        Assert.That(tracker.StarvedMilliseconds, Is.Zero,
            "80 ms of queued audio covers a 5 ms gap with plenty to spare. This is every normal iteration.");
    }

    [Test]
    public void CountsTheGapBeyondWhatWasQueued()
    {
        var tracker = new DeviceStarvationTracker();

        tracker.Observe(gapMs: 100, playableMs: 80, anythingPlaying: true);

        Assert.That(tracker.StarvedMilliseconds, Is.EqualTo(20).Within(0.01),
            "The device can play at most what it was holding; the rest of the gap is silence.");
    }

    /// <summary>
    /// A case that GC pause long enough to empty the queue also suspends the mix loop.
    /// </summary>
    [Test]
    public void ReportsALongFreezeInFullRatherThanAsOneTick()
    {
        var tracker = new DeviceStarvationTracker();

        tracker.Observe(gapMs: 700, playableMs: 80, anythingPlaying: true);

        Assert.That(tracker.StarvedMilliseconds, Is.EqualTo(620).Within(0.01),
            "A 700 ms freeze against an 80 ms queue is 620 ms of silence, and the whole point is that the thread "
            + "reporting it was not running for any of it.");
    }

    [Test]
    public void IgnoresAnIdleDevice()
    {
        var tracker = new DeviceStarvationTracker();

        tracker.Observe(gapMs: 700, playableMs: 0, anythingPlaying: false);

        Assert.That(tracker.StarvedMilliseconds, Is.Zero,
            "An empty queue with nothing playing is a device with nothing to do, not a dropout.");
    }

    [Test]
    public void Accumulates()
    {
        var tracker = new DeviceStarvationTracker();

        tracker.Observe(gapMs: 100, playableMs: 80, anythingPlaying: true);
        tracker.Observe(gapMs: 50, playableMs: 10, anythingPlaying: true);
        tracker.Observe(gapMs: 5, playableMs: 80, anythingPlaying: true);

        Assert.That(tracker.StarvedMilliseconds, Is.EqualTo(60).Within(0.01));
    }

    [Test]
    public void TreatsAnImpossibleQueueDepthAsEmpty()
    {
        var tracker = new DeviceStarvationTracker();

        // SDL_GetAudioStreamQueued returns -1 on error, and the manager's conversion could in principle
        // pass a negative through. It must not be able to hide starvation by making the gap look covered.
        tracker.Observe(gapMs: 30, playableMs: -100, anythingPlaying: true);

        Assert.That(tracker.StarvedMilliseconds, Is.EqualTo(30).Within(0.01));
    }

    [TestCase(620.0, 10.0, 62)]
    [TestCase(620.0, 21.33, 29)]
    [TestCase(5.0, 10.0, 0)]
    public void ExpressesStarvationAsACountOfMissedPeriods(double starvedMs, double periodMs, long expected)
    {
        var tracker = new DeviceStarvationTracker();

        tracker.Observe(starvedMs, 0, anythingPlaying: true);

        Assert.That(tracker.CountIn(periodMs), Is.EqualTo(expected));
    }

    [Test]
    public void CountIsZeroForANonsensePeriod()
    {
        var tracker = new DeviceStarvationTracker();

        tracker.Observe(gapMs: 620, playableMs: 0, anythingPlaying: true);

        Assert.That(tracker.CountIn(0), Is.Zero, "A zero-length period would otherwise divide by zero.");
    }
}
