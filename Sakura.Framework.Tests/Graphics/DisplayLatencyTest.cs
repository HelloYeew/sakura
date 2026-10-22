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

    [Test]
    public void TestBetweenTwoTimestamps()
    {
        long content = 1_000_000;
        long shown = content + Stopwatch.Frequency / 20;

        Assert.That(DisplayLatency.Between(content, shown), Is.EqualTo(50).Within(0.001));
    }
}
