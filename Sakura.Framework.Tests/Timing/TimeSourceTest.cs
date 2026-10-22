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
        double between = Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;

        // The conversion of "now" must land between the two surrounding reads, which are as far apart as the thread
        // was held up between them.
        Assert.That(converted, Is.LessThanOrEqualTo(now));
        Assert.That(now - converted, Is.LessThanOrEqualTo(between + 1), "Conversion should be on the same timeline");
    }

    [Test]
    public void TestClocksCreatedAtDifferentTimesShareTimeline()
    {
        // The core guarantee that makes cross-thread time comparison valid:
        // two always-running clocks report (almost) identical absolute times,
        // regardless of when they were constructed.
        long before = Stopwatch.GetTimestamp();
        var first = new Clock(true);
        double firstMade = Stopwatch.GetElapsedTime(before).TotalMilliseconds;

        Thread.Sleep(20);

        before = Stopwatch.GetTimestamp();
        var second = new Clock(true);
        double secondMade = Stopwatch.GetElapsedTime(before).TotalMilliseconds;

        before = Stopwatch.GetTimestamp();
        first.ProcessFrame();
        second.ProcessFrame();
        double between = Stopwatch.GetElapsedTime(before).TotalMilliseconds;

        // Bounded by what was measured rather than a fixed margin, which a held-up thread can exceed. The second is
        // read after the first, so it can be ahead by as long as that took; and a clock counts from the timeline less
        // the time between the reads in its constructor and in Start, so each can be behind by at most how long it
        // took to make. Clocks counting from their own creation would put the second about 20 ms behind instead.
        Assert.That(second.CurrentTime - first.CurrentTime, Is.InRange(-secondMade - 1, between + firstMade + 1),
            "Always-running clocks must share the same timeline");
    }
}
