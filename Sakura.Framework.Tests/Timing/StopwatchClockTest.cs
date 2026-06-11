// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Threading;
using NUnit.Framework;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Timing;

[TestFixture]
public class StopwatchClockTest
{
    [Test]
    public void TestStartsAtZero()
    {
        var clock = new StopwatchClock();
        Assert.That(clock.CurrentTime, Is.EqualTo(0).Within(1));
        Assert.That(clock.IsRunning, Is.False);
    }

    [Test]
    public void TestAdvancesWhenRunning()
    {
        var clock = new StopwatchClock(start: true);
        Thread.Sleep(50);
        Assert.That(clock.CurrentTime, Is.GreaterThan(0));
    }

    [Test]
    public void TestDoesNotAdvanceWhenStopped()
    {
        var clock = new StopwatchClock(start: true);
        Thread.Sleep(30);
        clock.Stop();
        double stoppedTime = clock.CurrentTime;
        Thread.Sleep(30);
        Assert.That(clock.CurrentTime, Is.EqualTo(stoppedTime).Within(1));
    }

    [Test]
    public void TestResetReturnsToZero()
    {
        var clock = new StopwatchClock(start: true);
        Thread.Sleep(30);
        clock.Stop();
        Assert.That(clock.CurrentTime, Is.GreaterThan(0));
        clock.Reset();
        Assert.That(clock.CurrentTime, Is.EqualTo(0).Within(1));
        Assert.That(clock.IsRunning, Is.False);
    }

    [Test]
    public void TestSeekWhileStopped()
    {
        var clock = new StopwatchClock();
        clock.Seek(5000);
        Assert.That(clock.CurrentTime, Is.EqualTo(5000).Within(1));
        Assert.That(clock.IsRunning, Is.False);
    }

    [Test]
    public void TestSeekWhileRunning()
    {
        var clock = new StopwatchClock(start: true);
        Thread.Sleep(30);
        clock.Seek(9000);
        Assert.That(clock.CurrentTime, Is.EqualTo(9000).Within(5));
        Assert.That(clock.IsRunning, Is.True);
        Thread.Sleep(30);
        Assert.That(clock.CurrentTime, Is.GreaterThan(9000));
    }

    [Test]
    public void TestSeekNegative()
    {
        var clock = new StopwatchClock();
        clock.Seek(-2000);
        Assert.That(clock.CurrentTime, Is.EqualTo(-2000).Within(1));
    }

    [Test]
    public void TestRateChangeDoesNotJumpTime()
    {
        var clock = new StopwatchClock(start: true);
        Thread.Sleep(30);
        clock.Stop();
        double timeBefore = clock.CurrentTime;
        clock.Rate = 2.0;
        Assert.That(clock.CurrentTime, Is.EqualTo(timeBefore).Within(1));
    }

    [Test]
    public void TestDoubleRateRunsFaster()
    {
        var clock = new StopwatchClock { Rate = 2.0 };
        clock.Start();
        Thread.Sleep(100);
        clock.Stop();
        Assert.That(clock.CurrentTime, Is.GreaterThan(150));
    }

    [Test]
    public void TestNegativeRateRunsBackwards()
    {
        var clock = new StopwatchClock { Rate = -1.0 };
        clock.Start();
        Thread.Sleep(50);
        clock.Stop();
        Assert.That(clock.CurrentTime, Is.LessThan(0));
    }
}
