// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Threading;
using NUnit.Framework;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Timing;

[TestFixture]
public class ClockTest
{
    [Test]
    public void TestAdvancesWhenRunning()
    {
        var clock = new Clock(true);
        Thread.Sleep(50);
        clock.ProcessFrame();
        Assert.That(clock.CurrentTime, Is.GreaterThan(0));
    }

    [Test]
    public void TestDoesNotAdvanceWhenStopped()
    {
        var clock = new Clock(true);
        Thread.Sleep(30);
        clock.ProcessFrame();
        clock.Stop();
        double stoppedTime = clock.CurrentTime;
        Thread.Sleep(30);
        clock.ProcessFrame();
        Assert.That(clock.CurrentTime, Is.EqualTo(stoppedTime).Within(1));
    }

    [Test]
    public void TestResetReturnsToZero()
    {
        var clock = new Clock(start: true);
        Thread.Sleep(50);
        clock.ProcessFrame();
        Assert.That(clock.CurrentTime, Is.GreaterThan(0));
        clock.Reset();
        Assert.That(clock.CurrentTime, Is.EqualTo(0).Within(1));
        Assert.That(clock.ElapsedFrameTime, Is.EqualTo(0).Within(1));
    }

    [Test]
    public void TestFirstFrameAfterResetHasSmallElapsed()
    {
        var clock = new Clock(start: true);
        Thread.Sleep(50);
        clock.ProcessFrame();
        clock.Reset();
        clock.ProcessFrame();
        Assert.That(clock.CurrentTime, Is.LessThan(20));
        Assert.That(clock.CurrentTime, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void TestContinuesRunningAfterReset()
    {
        var clock = new Clock(start: true);
        Thread.Sleep(30);
        clock.ProcessFrame();
        clock.Reset();
        Thread.Sleep(50);
        clock.ProcessFrame();
        Assert.That(clock.IsRunning, Is.True);
        Assert.That(clock.CurrentTime, Is.GreaterThan(0));
        Assert.That(clock.CurrentTime, Is.LessThan(100));
    }
}
