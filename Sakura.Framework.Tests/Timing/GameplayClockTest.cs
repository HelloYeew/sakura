// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Timing;

/// <summary>
/// Tests for <see cref="GameplayClock"/> — the full precision chain used for rhythm gameplay.
/// All tests use a <see cref="ManualClock"/> as the real-time reference for determinism.
/// </summary>
[TestFixture]
public class GameplayClockTest
{
    private TestAdjustableClock source = null!;
    private ManualClock reference = null!;
    private GameplayClock gameplay = null!;

    [SetUp]
    public void SetUp()
    {
        source = new TestAdjustableClock();
        reference = new ManualClock { CurrentTime = 0, IsRunning = true };
        gameplay = new GameplayClock(source, reference);
    }

    #region Basic controls

    [Test]
    public void TestStartAndStop()
    {
        Assert.That(gameplay.IsRunning, Is.False);

        gameplay.Start();
        gameplay.ProcessFrame();
        Assert.That(gameplay.IsRunning, Is.True);

        gameplay.Stop();
        Assert.That(gameplay.IsRunning, Is.False);
    }

    [Test]
    public void TestSeekForward()
    {
        gameplay.Start();
        gameplay.Seek(3000);
        gameplay.ProcessFrame();

        Assert.That(gameplay.CurrentTime, Is.EqualTo(3000).Within(5));
    }

    [Test]
    public void TestReset()
    {
        source.Start();
        source.AdvanceBy(1000);
        gameplay.Start();
        gameplay.ProcessFrame();
        gameplay.Reset();
        gameplay.ProcessFrame();

        Assert.That(gameplay.IsRunning, Is.False);
        Assert.That(gameplay.CurrentTime, Is.EqualTo(0).Within(1));
    }

    #endregion

    #region Offset

    [Test]
    public void TestOffsetShiftsCurrentTime()
    {
        source.Start();
        source.AdvanceBy(1000);
        gameplay.Start();
        gameplay.ProcessFrame();

        double baseTime = gameplay.CurrentTime;
        gameplay.Offset = 20;

        Assert.That(gameplay.CurrentTime, Is.EqualTo(baseTime + 20).Within(2));
    }

    [Test]
    public void TestOffsetDoesNotAffectDecoupledOrInterpolatedInternals()
    {
        gameplay.Offset = 50;
        source.Start();
        source.AdvanceBy(500);
        gameplay.Start();
        gameplay.ProcessFrame();

        // Internals are unaffected; only the exposed CurrentTime shifts.
        Assert.That(gameplay.InterpolatedClock.CurrentTime, Is.EqualTo(gameplay.CurrentTime - 50).Within(2));
    }

    #endregion

    #region Lead-in (negative time)

    [Test]
    public void TestNegativeLeadIn()
    {
        gameplay.Start();
        gameplay.Seek(-500);
        gameplay.ProcessFrame();

        Assert.That(gameplay.CurrentTime, Is.EqualTo(-500).Within(5));
        Assert.That(gameplay.DecoupledClock.IsDecoupled, Is.True);
        Assert.That(source.IsRunning, Is.False);
    }

    [Test]
    public void TestSourceStartsAfterLeadIn()
    {
        gameplay.Start();
        gameplay.Seek(-100);
        gameplay.ProcessFrame();

        reference.CurrentTime += 200;
        gameplay.ProcessFrame();

        Assert.That(source.IsRunning, Is.True);
        Assert.That(gameplay.CurrentTime, Is.GreaterThan(0));
    }

    #endregion

    #region Rate

    [Test]
    public void TestRatePropagatesToChain()
    {
        gameplay.Rate = 1.5;
        Assert.That(gameplay.Rate, Is.EqualTo(1.5).Within(0.001));
        Assert.That(gameplay.DecoupledClock.Rate, Is.EqualTo(1.5).Within(0.001));
    }

    #endregion

    #region GetTimeAt

    [Test]
    public void TestGetTimeAtCurrentReferenceEqualsCurrentTime()
    {
        source.Start();
        source.AdvanceBy(500);
        gameplay.Start();
        gameplay.ProcessFrame();

        // An event that happened exactly at this frame's reference time maps to CurrentTime.
        double result = gameplay.GetTimeAt(reference.CurrentTime);
        Assert.That(result, Is.EqualTo(gameplay.CurrentTime).Within(2));
    }

    [Test]
    public void TestGetTimeAtEarlierTimestampProducesEarlierGameplayTime()
    {
        source.Start();
        source.AdvanceBy(1000);
        gameplay.Start();
        gameplay.ProcessFrame();

        // An event that happened 10 ms before this frame should map to 10 ms earlier.
        double result = gameplay.GetTimeAt(reference.CurrentTime - 10);
        Assert.That(result, Is.LessThan(gameplay.CurrentTime));
        Assert.That(gameplay.CurrentTime - result, Is.EqualTo(10).Within(1));
    }

    #endregion

    #region Smooth interpolation through chain

    [Test]
    public void TestCurrentTimeNeverRunsBackwardsDuringPlayback()
    {
        source.Start();
        source.AdvanceBy(100);
        gameplay.Start();
        gameplay.ProcessFrame();

        double last = gameplay.CurrentTime;

        for (int i = 0; i < 20; i++)
        {
            // Advance source and reference by the same amount so the interpolator
            // stays in smooth mode and never snaps backwards.
            source.AdvanceBy(8);
            reference.CurrentTime += 8;
            gameplay.ProcessFrame();

            Assert.That(gameplay.CurrentTime, Is.GreaterThanOrEqualTo(last), "Gameplay time must never run backwards");
            last = gameplay.CurrentTime;
        }
    }

    #endregion
}
