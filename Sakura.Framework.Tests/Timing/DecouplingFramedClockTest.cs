// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
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
    public void TestSourceThatRunsOutIsNotRestarted()
    {
        source.Length = 1000;
        decoupled.Start();
        decoupled.Seek(900);
        decoupled.ProcessFrame();

        // The track plays to just short of its reported end and stops by itself.
        source.AdvanceBy(80);
        reference.CurrentTime += 80;
        decoupled.ProcessFrame();
        int startsBefore = source.StartCount;
        source.RunOut();

        // Still inside the track's range for a frame (so a seek would succeed), then past its end.
        for (int i = 0; i < 5; i++)
        {
            reference.CurrentTime += 10;
            decoupled.ProcessFrame();
        }

        Assert.That(source.StartCount, Is.EqualTo(startsBefore), "a finished track isn't started again");
        Assert.That(source.IsRunning, Is.False);
        Assert.That(decoupled.IsDecoupled, Is.True);
        Assert.That(decoupled.CurrentTime, Is.EqualTo(1030).Within(1), "time carries on past the end");
    }

    [Test]
    public void TestSeekAfterSourceRanOutStartsItAgain()
    {
        source.Length = 1000;
        decoupled.Start();
        decoupled.Seek(990);
        decoupled.ProcessFrame();
        source.RunOut();
        reference.CurrentTime += 50;
        decoupled.ProcessFrame();

        decoupled.Seek(200);
        decoupled.ProcessFrame();

        Assert.That(source.IsRunning, Is.True);
        Assert.That(source.CurrentTime, Is.EqualTo(200).Within(1));
    }

    [Test]
    public void TestSeekPastSourceEndDoesNotStartIt()
    {
        source.Length = 1000;
        decoupled.Start();
        decoupled.ProcessFrame();
        source.Stop();

        decoupled.Seek(1500);
        reference.CurrentTime += 10;
        decoupled.ProcessFrame();

        Assert.That(source.IsRunning, Is.False);
        Assert.That(decoupled.CurrentTime, Is.EqualTo(1510).Within(1));
    }

    [Test]
    [FlakyTest]
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

    #region Output latency

    /// <summary>
    /// A track whose time is what's heard: its mix runs <see cref="OutputLatency"/> ahead, as on a real output, so
    /// started from a position it only reports it that much later.
    /// </summary>
    private sealed class HeardTestClock : IAdjustableClock, IHasOutputLatency
    {
        private double mixed;

        public double OutputLatency { get; set; }
        public double CurrentTime => Math.Max(0, mixed - OutputLatency * Rate);
        public double Rate { get; set; } = 1;
        public bool IsRunning { get; private set; }
        public double LastSeek { get; private set; } = double.NaN;

        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;

        public double Length { get; set; } = double.MaxValue;

        public bool Seek(double position)
        {
            if (position < 0 || position > Length)
                return false;

            mixed = position;
            LastSeek = position;
            return true;
        }

        public void Reset()
        {
            IsRunning = false;
            mixed = 0;
        }

        /// <summary>
        /// Real time passing for the output: a running track mixes on.
        /// </summary>
        public void Pass(double milliseconds)
        {
            if (IsRunning)
                mixed += milliseconds * Rate;
        }
    }

    [Test]
    public void TestResumingCarriesStraightOnIntoWhatsHeard()
    {
        var track = new HeardTestClock { OutputLatency = 40 };
        var clock = new DecouplingFramedClock(track, reference);

        clock.Seek(1000);
        clock.Start();

        Assert.That(track.LastSeek, Is.EqualTo(1040), "The track wasn't started ahead by its latency");

        clock.ProcessFrame();
        Assert.That(clock.CurrentTime, Is.EqualTo(1000), "Time stepped back by the latency as play resumed");

        reference.CurrentTime += 10;
        track.Pass(10);
        clock.ProcessFrame();

        Assert.That(clock.CurrentTime, Is.EqualTo(1010));
    }

    [Test]
    public void TestSeekingWhilePlayingLandsWhereAsked()
    {
        var track = new HeardTestClock { OutputLatency = 40 };
        var clock = new DecouplingFramedClock(track, reference);

        clock.Seek(500);
        clock.Start();
        clock.ProcessFrame();

        clock.Seek(2000);
        clock.ProcessFrame();

        Assert.That(track.LastSeek, Is.EqualTo(2040));
        Assert.That(clock.CurrentTime, Is.EqualTo(2000));
    }

    [Test]
    public void TestLeadInStartsTheTrackSoItsStartIsHeardAtZero()
    {
        var track = new HeardTestClock { OutputLatency = 40 };
        var clock = new DecouplingFramedClock(track, reference);

        clock.Seek(-100);
        clock.Start();
        clock.ProcessFrame();

        double last = clock.CurrentTime;

        for (int frame = 1; frame <= 20; frame++)
        {
            reference.CurrentTime += 10;
            track.Pass(10);
            clock.ProcessFrame();

            // Steady through the hand-over: 10 ms a frame, no step back to zero and no pause there.
            Assert.That(clock.CurrentTime - last, Is.EqualTo(10).Within(0.001), $"Frame {frame} moved by {clock.CurrentTime - last} ms");
            last = clock.CurrentTime;

            if (clock.CurrentTime < -40)
                Assert.That(track.IsRunning, Is.False, "Started earlier than its latency asks");
        }

        Assert.That(track.LastSeek, Is.EqualTo(0).Within(0.001), "The track wasn't started from its beginning");
        Assert.That(clock.CurrentTime, Is.EqualTo(100).Within(0.001));
        Assert.That(clock.IsDecoupled, Is.False, "Still running on its own past zero rather than following the track");
    }

    [Test]
    public void TestSeekingCloserToTheEndThanTheLatencyStillLands()
    {
        var track = new HeardTestClock { OutputLatency = 40, Length = 10_000 };
        var clock = new DecouplingFramedClock(track, reference);

        clock.Seek(1000);
        clock.Start();
        clock.ProcessFrame();

        // Ahead by the latency, that's past the end, which the track refuses: it goes there unled rather than not at all.
        clock.Seek(9990);
        clock.ProcessFrame();

        Assert.That(track.LastSeek, Is.EqualTo(9990));
        Assert.That(clock.CurrentTime, Is.GreaterThan(9900), "The seek was lost and the clock went back to where the track still was");
    }

    [Test]
    public void TestATrackWithNoLatencyStartsWhereTheClockIs()
    {
        var track = new HeardTestClock();
        var clock = new DecouplingFramedClock(track, reference);

        clock.Seek(1000);
        clock.Start();

        Assert.That(track.LastSeek, Is.EqualTo(1000));
    }

    #endregion
}
