// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Diagnostics;
using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// How long frames take to reach the screen, settled from the frames a renderer can tell were shown.
/// </summary>
[TestFixture]
public class DisplayLatencyTest
{
    [Test]
    public void TestNothingUntilAFrameIsShown()
    {
        var latency = new DisplayLatency("a test");

        Assert.That(latency.Milliseconds, Is.Null);
    }

    /// <summary>
    /// Readings spread by where in the refresh their update landed; the figure settles on their average.
    /// </summary>
    [Test]
    public void TestSettlesOnTheAverageOfARefreshsSpread()
    {
        var latency = new DisplayLatency("a test");

        for (int i = 0; i < 400; i++)
            latency.Add(i % 2 == 0 ? 40 : 56);

        Assert.That(latency.Milliseconds, Is.EqualTo(48).Within(1));
    }

    /// <summary>
    /// A frame shown after a stall, or a reading from clocks that don't agree, says nothing about the display.
    /// </summary>
    [TestCase(0)]
    [TestCase(-5)]
    [TestCase(900)]
    public void TestImpossibleReadingsAreIgnored(double reading)
    {
        var latency = new DisplayLatency("a test");
        latency.Add(45);
        latency.Add(reading);

        Assert.That(latency.Milliseconds, Is.EqualTo(45));
    }

    /// <summary>
    /// One slow frame far from the rest moves nothing; a display that changed keeps landing there, and is taken at once.
    /// </summary>
    [Test]
    public void TestAStrayFrameIsIgnoredButAChangeIsTaken()
    {
        var latency = new DisplayLatency("a test");

        for (int i = 0; i < 50; i++)
            latency.Add(45);

        latency.Add(120);
        Assert.That(latency.Milliseconds, Is.EqualTo(45).Within(0.001), "one stray frame");

        for (int i = 0; i < 10; i++)
            latency.Add(16);

        Assert.That(latency.Milliseconds, Is.EqualTo(16).Within(0.001), "vsync turned off");
    }

    /// <summary>
    /// Present times that come in nanoseconds of the monotonic clock (Android, Linux) as Stopwatch timestamps.
    /// </summary>
    [Test]
    public void TestNanosecondsAsStopwatchTicks()
    {
        long ticks = DisplayLatency.TicksFromNanoseconds(2_500_000_000);

        Assert.That(ticks / (double)Stopwatch.Frequency, Is.EqualTo(2.5).Within(1e-6));
    }

    [Test]
    public void TestBetweenTwoTimestamps()
    {
        long content = 1_000_000;
        long shown = content + Stopwatch.Frequency / 20;

        Assert.That(DisplayLatency.Between(content, shown), Is.EqualTo(50).Within(0.001));
    }

    /// <summary>
    /// A frame measured on screen counts towards the figure, and is kept for the host to split, oldest first.
    /// </summary>
    [Test]
    public void TestShownFramesAreKeptForTheHost()
    {
        var latency = new DisplayLatency("a test");
        latency.AddFrame(1_000, 1_000 + Stopwatch.Frequency / 25);
        latency.AddFrame(2_000, 2_000 + Stopwatch.Frequency / 25);

        Assert.That(latency.Milliseconds, Is.EqualTo(40).Within(0.001));

        Assert.That(latency.TryTakeShown(out var frame), Is.True);
        Assert.That(frame.Content, Is.EqualTo(1_000));
        Assert.That(frame.Shown, Is.EqualTo(1_000 + Stopwatch.Frequency / 25));
        Assert.That(frame.GpuDone, Is.EqualTo(0));

        Assert.That(latency.TryTakeShown(out frame), Is.True);
        Assert.That(frame.Content, Is.EqualTo(2_000));

        Assert.That(latency.TryTakeShown(out _), Is.False);
    }

    /// <summary>
    /// A frame that can't be a latency isn't kept, and a host that falls behind gets the newest frames.
    /// </summary>
    [Test]
    public void TestOnlyTheNewestPossibleFramesAreKept()
    {
        var latency = new DisplayLatency("a test");
        latency.AddFrame(5_000, 4_000);

        Assert.That(latency.TryTakeShown(out _), Is.False, "shown before its update");

        for (long i = 1; i <= 20; i++)
            latency.AddFrame(i, i + Stopwatch.Frequency / 50);

        Assert.That(latency.TryTakeShown(out var frame), Is.True);
        Assert.That(frame.Content, Is.EqualTo(13), "the oldest of the newest eight");
    }
}
