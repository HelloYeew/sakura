// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Threading;
using NUnit.Framework;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Timing;

/// <summary>
/// Tests for <see cref="DecouplingFramedClock"/>.
/// </summary>
[TestFixture]
public class DecouplingFramedClockTest
{
    private TestAdjustableClock source = null!;
    private ManualClock reference = null!;
    private DecouplingFramedClock decoupled = null!;

    [SetUp]
    public void SetUp()
    {
        source = new TestAdjustableClock();
        reference = new ManualClock { CurrentTime = 0, IsRunning = true };
        decoupled = new DecouplingFramedClock(source, reference);
    }

    #region Basic behaviour

    [Test]
    public void TestStartStartsSource()
    {
        Assert.That(source.IsRunning, Is.False);
        Assert.That(decoupled.IsRunning, Is.False);

        decoupled.Start();
        decoupled.ProcessFrame();

        Assert.That(source.IsRunning, Is.True);
        Assert.That(decoupled.IsRunning, Is.True);
    }

    [Test]
    public void TestStopStopsSource()
    {
        decoupled.Start();
        decoupled.ProcessFrame();

        decoupled.Stop();

        Assert.That(decoupled.IsRunning, Is.False);
        Assert.That(source.IsRunning, Is.False);
    }

    [Test]
    public void TestSeekForwardsWhileRunning()
    {
        decoupled.Start();
        decoupled.Seek(1000);
        decoupled.ProcessFrame();

        Assert.That(decoupled.CurrentTime, Is.EqualTo(1000).Within(1));
        Assert.That(source.CurrentTime, Is.EqualTo(1000).Within(1));
    }

    [Test]
    public void TestResetGoesToZeroAndStops()
    {
        source.Start();
        source.AdvanceBy(2000);
        decoupled.Start();
        decoupled.ProcessFrame();

        decoupled.Reset();
        decoupled.ProcessFrame();

        Assert.That(decoupled.IsRunning, Is.False);
        Assert.That(decoupled.CurrentTime, Is.EqualTo(0).Within(1));
        Assert.That(source.CurrentTime, Is.EqualTo(0).Within(1));
    }

    #endregion

    #region Decoupled (negative lead-in) behaviour

    [Test]
    public void TestSeekNegativeEntersDecoupledMode()
    {
        decoupled.Start();
        decoupled.Seek(-500);
        decoupled.ProcessFrame();

        Assert.That(decoupled.CurrentTime, Is.EqualTo(-500).Within(1));
        Assert.That(decoupled.IsDecoupled, Is.True);
        Assert.That(source.IsRunning, Is.False);
        Assert.That(source.CurrentTime, Is.EqualTo(0).Within(1));
    }

    [Test]
    public void TestDecoupledTimeAdvancesWithReference()
    {
        decoupled.Start();
        decoupled.Seek(-300);
        decoupled.ProcessFrame();

        double timeBefore = decoupled.CurrentTime;

        // Advance the reference by 100 ms.
        reference.CurrentTime += 100;
        decoupled.ProcessFrame();

        Assert.That(decoupled.CurrentTime, Is.GreaterThan(timeBefore));
        Assert.That(decoupled.CurrentTime, Is.EqualTo(timeBefore + 100).Within(2));
    }

    [Test]
    public void TestSourceStartsAutomaticallyWhenDecoupledTimeCrossesZero()
    {
        decoupled.Start();
        decoupled.Seek(-50);
        decoupled.ProcessFrame();

        Assert.That(source.IsRunning, Is.False);

        // Advance reference past zero.
        reference.CurrentTime += 100;
        decoupled.ProcessFrame();

        Assert.That(source.IsRunning, Is.True);

        // IsDecoupled is updated on the *next* ProcessFrame after the source starts,
        // because the handover happens mid-frame and the flag reflects what drove the
        // *current* frame's time value.
        reference.CurrentTime += 10;
        decoupled.ProcessFrame();

        Assert.That(decoupled.IsDecoupled, Is.False);
    }

    [Test]
    public void TestDecoupledDoesNotDriftFromReference()
    {
        var realReference = new ManualClock { CurrentTime = 0, IsRunning = true };
        var clock = new DecouplingFramedClock(source, realReference);
        clock.Start();
        clock.Seek(-200);
        clock.ProcessFrame();

        double clockTime = clock.CurrentTime;

        for (int i = 0; i < 20; i++)
        {
            realReference.CurrentTime += 10;
            clock.ProcessFrame();

            Assert.That(clock.CurrentTime, Is.EqualTo(clockTime + (i + 1) * 10).Within(1),
                "Decoupled time must track the reference with no drift");
        }
    }

    [Test]
    public void TestChangeSourceUpdatesToNewSourceTime()
    {
        // Start the decoupled clock first (CurrentTime = 0), then advance the source
        // so ProcessFrame picks it up at 1000 without a mid-start seek clobbering it.
        decoupled.Start();
        source.Start();
        source.AdvanceBy(1000);
        decoupled.ProcessFrame();

        Assert.That(decoupled.CurrentTime, Is.EqualTo(1000).Within(1));

        var secondSource = new TestAdjustableClock();
        secondSource.AdvanceBy(500);
        secondSource.Start();

        decoupled.ChangeSource(secondSource);
        decoupled.ProcessFrame();

        Assert.That(decoupled.CurrentTime, Is.EqualTo(500).Within(5));
    }

    [Test]
    public void TestElapsedFrameTimeIsZeroWhenStopped()
    {
        decoupled.Start();
        decoupled.ProcessFrame();
        decoupled.Stop();
        reference.CurrentTime += 100;
        decoupled.ProcessFrame();

        Assert.That(decoupled.ElapsedFrameTime, Is.EqualTo(0));
    }

    [Test]
    public void TestStartFromNegativeTimeIncrementsCorrectlyWithRealClock()
    {
        // Use a real reference clock this time to test actual timing behaviour.
        Thread.Sleep(100);

        var realDecoupled = new DecouplingFramedClock(source);
        realDecoupled.Start();
        realDecoupled.Seek(-200);
        realDecoupled.ProcessFrame();

        Assert.That(realDecoupled.IsRunning, Is.True);
        Assert.That(realDecoupled.CurrentTime, Is.LessThan(0));

        double previousTime = realDecoupled.CurrentTime;

        Thread.Sleep(50);
        realDecoupled.ProcessFrame();

        Assert.That(realDecoupled.CurrentTime, Is.GreaterThan(previousTime));
        Assert.That(realDecoupled.ElapsedFrameTime, Is.GreaterThan(0));
    }

    #endregion
}
