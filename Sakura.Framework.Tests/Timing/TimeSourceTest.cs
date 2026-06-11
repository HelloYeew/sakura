// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Timing;

[TestFixture]
public class TimeSourceTest
{
    [Test]
    public void TestMonotonic()
    {
        double last = TimeSource.CurrentTime;

        for (int i = 0; i < 1000; i++)
        {
            double now = TimeSource.CurrentTime;
            Assert.That(now, Is.GreaterThanOrEqualTo(last), "TimeSource must never run backwards");
            last = now;
        }
    }

    [Test]
    public void TestFromStopwatchTimestampMatchesCurrentTime()
    {
        long timestamp = Stopwatch.GetTimestamp();
        double converted = TimeSource.FromStopwatchTimestamp(timestamp);
        double now = TimeSource.CurrentTime;

        // The conversion of "now" must land between the two surrounding reads.
        Assert.That(converted, Is.LessThanOrEqualTo(now));
        Assert.That(now - converted, Is.LessThan(50), "Conversion should be on the same timeline");
    }

    [Test]
    public void TestClocksCreatedAtDifferentTimesShareTimeline()
    {
        // The core guarantee that makes cross-thread time comparison valid:
        // two always-running clocks report (almost) identical absolute times,
        // regardless of when they were constructed.
        var first = new Clock(true);
        Thread.Sleep(20);
        var second = new Clock(true);

        first.ProcessFrame();
        second.ProcessFrame();

        Assert.That(first.CurrentTime, Is.EqualTo(second.CurrentTime).Within(20), "Always-running clocks must share the same timeline");
    }
}
