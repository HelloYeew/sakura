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
}
