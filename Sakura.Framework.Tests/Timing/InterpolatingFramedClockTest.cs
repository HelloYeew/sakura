// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Threading;
using NUnit.Framework;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Timing;

/// <summary>
/// Tests for <see cref="InterpolatingFramedClock"/>.
/// A <see cref="ManualClock"/> reference lets tests control time precisely without
/// relying on wall-clock sleeps except where real-time behaviour is explicitly tested.
/// </summary>
[TestFixture]
public class InterpolatingFramedClockTest
{
    private TestAdjustableClock source = null!;
    private ManualClock reference = null!;
    private InterpolatingFramedClock interpolating = null!;

    [SetUp]
    public void SetUp()
    {
        source = new TestAdjustableClock();
        reference = new ManualClock { CurrentTime = 0, IsRunning = true };
        interpolating = new InterpolatingFramedClock(source, reference);
    }

    #region Stopped source

    [Test]
    public void TestReportsSourceTimeWhenStopped()
    {
        source.Seek(500);
        reference.CurrentTime += 50;
        interpolating.ProcessFrame();

        Assert.That(interpolating.CurrentTime, Is.EqualTo(500).Within(1));
        Assert.That(interpolating.IsInterpolating, Is.False);
    }

    #endregion

    #region Basic interpolation

    [Test]
    public void TestFirstFrameSnapsToSource()
    {
        source.Start();
        source.AdvanceBy(200);
        interpolating.ProcessFrame();

        Assert.That(interpolating.CurrentTime, Is.EqualTo(200).Within(1));
        Assert.That(interpolating.IsInterpolating, Is.False);
    }

    [Test]
    public void TestSmoothlyInterpolatesBetweenChunkySourceUpdates()
    {
        source.Start();
        source.AdvanceBy(10);
        interpolating.ProcessFrame(); // First frame: snap to source.

        // Advance reference as if 5 ms has elapsed — source hasn't updated.
        reference.CurrentTime += 5;
        interpolating.ProcessFrame();

        Assert.That(interpolating.IsInterpolating, Is.True);
        Assert.That(interpolating.CurrentTime, Is.EqualTo(15).Within(2));
    }

    [Test]
    public void TestNeverRunsBackwardsWhilePlayingForward()
    {
        source.Start();
        source.AdvanceBy(100);
        interpolating.ProcessFrame();

        double last = interpolating.CurrentTime;

        for (int i = 0; i < 20; i++)
        {
            // Both source and reference advance by the same amount — no divergence that would
            // trigger a snap. The invariant under test is that CurrentTime never decreases.
            source.AdvanceBy(8);
            reference.CurrentTime += 8;
            interpolating.ProcessFrame();

            Assert.That(interpolating.CurrentTime, Is.GreaterThanOrEqualTo(last),
                "Interpolated time must never run backwards");
            last = interpolating.CurrentTime;
        }
    }

    [Test]
    public void TestStaysWithinAllowableError()
    {
        source.Start();
        source.AdvanceBy(100);
        interpolating.ProcessFrame();

        for (int i = 0; i < 30; i++)
        {
            source.AdvanceBy(8);
            reference.CurrentTime += 8;
            interpolating.ProcessFrame();

            Assert.That(Math.Abs(interpolating.CurrentTime - source.CurrentTime),
                Is.LessThanOrEqualTo(interpolating.AllowableErrorMilliseconds),
                "Interpolated time must stay within allowable error of source");
        }
    }

    #endregion

    #region Seek / discontinuity

    [Test]
    public void TestSeekForwardSnaps()
    {
        source.Start();
        source.AdvanceBy(100);
        interpolating.ProcessFrame(); // Baseline.

        source.Seek(10000);
        interpolating.ProcessFrame();

        Assert.That(interpolating.CurrentTime, Is.EqualTo(10000).Within(1));
        Assert.That(interpolating.IsInterpolating, Is.False);
    }

    [Test]
    public void TestSeekBackwardSnaps()
    {
        source.Start();
        source.AdvanceBy(5000);
        interpolating.ProcessFrame();
        source.Seek(0);
        interpolating.ProcessFrame();

        Assert.That(interpolating.CurrentTime, Is.EqualTo(0).Within(1));
        Assert.That(interpolating.IsInterpolating, Is.False);
    }

    [Test]
    public void TestSeekBackwardAfterInterpolatingDoesNotGoForward()
    {
        // Advance enough to trigger real interpolation.
        source.Start();
        source.AdvanceBy(100);
        interpolating.ProcessFrame();
        reference.CurrentTime += 5;
        interpolating.ProcessFrame();
        Assert.That(interpolating.IsInterpolating, Is.True);

        source.Stop();
        source.Seek(50);
        interpolating.ProcessFrame();

        Assert.That(interpolating.CurrentTime, Is.EqualTo(50).Within(1));
        Assert.That(interpolating.IsInterpolating, Is.False);
    }

    #endregion

    #region Change source

    [Test]
    public void TestChangeSourceAdoptsNewSourceTime()
    {
        source.Start();
        source.AdvanceBy(1000);
        interpolating.ProcessFrame();

        var secondSource = new TestAdjustableClock();
        secondSource.AdvanceBy(300);

        interpolating.ChangeSource(secondSource);
        interpolating.ProcessFrame();

        Assert.That(interpolating.CurrentTime, Is.EqualTo(300).Within(1));
        Assert.That(interpolating.IsInterpolating, Is.False);
    }

    #endregion

    #region Real-time drift guard

    [Test]
    [FlakyTest]
    public void TestNoInterpolationDriftAgainstRealClock()
    {
        // Use a real StopwatchClock as source so we can verify there is no
        // accumulated drift when the source and reference run at the same rate.
        var realSource = new StopwatchClock(start: true);
        var realInterpolating = new InterpolatingFramedClock(realSource);

        realInterpolating.ProcessFrame(); // First frame baseline.

        for (int i = 0; i < 10; i++)
        {
            Thread.Sleep(10);
            realInterpolating.ProcessFrame();

            Assert.That(realInterpolating.CurrentTime,
                Is.EqualTo(realSource.CurrentTime).Within(realInterpolating.AllowableErrorMilliseconds),
                "Interpolated time must not drift beyond allowable error");
        }
    }

    #endregion

    #region Drift recovery

    [Test]
    public void TestClosesAGapWithTheSourceWithoutSnapping()
    {
        source.Start();
        source.AdvanceBy(100);
        interpolating.ProcessFrame();

        // The source moves ahead by less than the allowable error, then both run in step: the gap is
        // absorbed rather than carried for good.
        source.AdvanceBy(8);

        for (int i = 0; i < 60; i++)
        {
            source.AdvanceBy(16);
            reference.CurrentTime += 16;
            interpolating.ProcessFrame();

            Assert.That(interpolating.IsInterpolating, Is.True, "A gap within the allowable error must not snap");
        }

        Assert.That(interpolating.CurrentTime, Is.EqualTo(source.CurrentTime).Within(0.5));
    }

    [Test]
    public void TestFollowsAnOutputCallingBackEvery20MillisecondsSmoothly()
    {
        // The source is an audio position that only moves once a callback, 20 ms of audio at a time (an
        // output off the fast path, or Bluetooth), read by frames 1/60 s apart.
        const double frame = 1000.0 / 60;
        const double callback = 20;

        source.Start();
        interpolating.ProcessFrame();

        double last = interpolating.CurrentTime;
        double worstStep = 0;
        double worstError = 0;

        for (int i = 1; i <= 600; i++)
        {
            double now = i * frame;
            reference.CurrentTime = now;
            source.AdvanceBy(Math.Floor(now / callback) * callback - source.CurrentTime);
            interpolating.ProcessFrame();

            Assert.That(interpolating.IsInterpolating, Is.True, $"Snapped at frame {i}: a callback's step is no discontinuity");

            worstStep = Math.Max(worstStep, Math.Abs(interpolating.CurrentTime - last - frame));
            worstError = Math.Max(worstError, Math.Abs(interpolating.CurrentTime - (now - callback / 2)));
            last = interpolating.CurrentTime;
        }

        // Frame to frame, the time moves within a few milliseconds of real time, and it stays around the middle of
        // each callback's step, where the audio actually is on average.
        Assert.That(worstStep, Is.LessThan(5));
        Assert.That(worstError, Is.LessThan(callback / 2 + 1));
    }

    [Test]
    public void TestWaitsForASourceThatWasStartedToMove()
    {
        source.Start();
        source.AdvanceBy(1000);
        interpolating.ProcessFrame();

        source.Stop();
        reference.CurrentTime += 16;
        interpolating.ProcessFrame();

        // Started again, the source holds where it was for a few frames, as a track does until the audio of where it
        // was put is decoded and reaches the output, give or take a rounding (moved ahead by its latency, and that taken
        // off again).
        source.Start();
        source.AdvanceBy(0.0001);

        for (int i = 0; i < 4; i++)
        {
            reference.CurrentTime += 16;
            interpolating.ProcessFrame();

            Assert.That(interpolating.CurrentTime, Is.EqualTo(1000).Within(0.001), "Ran on ahead of a source that hadn't moved yet");
        }

        source.AdvanceBy(16);
        reference.CurrentTime += 16;
        interpolating.ProcessFrame();
        Assert.That(interpolating.CurrentTime, Is.EqualTo(1016).Within(0.001));

        source.AdvanceBy(16);
        reference.CurrentTime += 16;
        interpolating.ProcessFrame();
        Assert.That(interpolating.IsInterpolating, Is.True, "Didn't go back to interpolating once the source moved");
        Assert.That(interpolating.CurrentTime, Is.EqualTo(1032).Within(1));
    }

    [Test]
    public void TestNeverThrowsNorRunsBackwardsAgainstAnErraticSource()
    {
        var random = new Random(1234);

        source.Start();
        source.AdvanceBy(1000);
        interpolating.ProcessFrame();

        double last = interpolating.CurrentTime;

        for (int i = 0; i < 20_000; i++)
        {
            double elapsed = random.NextDouble() * 50;
            reference.CurrentTime += elapsed;

            // Mostly in step, now and then stalling or jumping either way by up to 40 ms.
            source.AdvanceBy(elapsed + (random.NextDouble() < 0.2 ? (random.NextDouble() - 0.5) * 80 : 0));

            Assert.DoesNotThrow(() => interpolating.ProcessFrame());

            if (interpolating.IsInterpolating)
                Assert.That(interpolating.CurrentTime, Is.GreaterThanOrEqualTo(last), "Interpolated time ran backwards");

            last = interpolating.CurrentTime;
        }
    }

    #endregion
}
