// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Audio.SdlEngine;

namespace Sakura.Framework.Tests.Audio;

[TestFixture]
public class MeasuredLatencyTest
{
    /// <summary>
    /// A frame heard 20 ms ago, with 960 frames (20 ms at 48 kHz) written since: the last of those is heard now plus
    /// nothing, and every frame written beyond that is another frame's time further off.
    /// </summary>
    [TestCase(960, 0.0)]
    [TestCase(960 + 480, 10.0)]
    [TestCase(960 + 4800, 100.0)]
    public void TestLatencyFromATimestamp(long framesWritten, double expectedMs)
    {
        const long now = 5_000_000_000;
        const long heardAt = now - 20_000_000;

        Assert.That(AAudioOutput.LatencyMilliseconds(1000, heardAt, 1000 + framesWritten, now, 48000), Is.EqualTo(expectedMs).Within(0.001));
    }

    /// <summary>
    /// A library from before the AAudio output says so rather than throwing, and anywhere but Android the output is
    /// never offered.
    /// </summary>
    [Test]
    public void TestAnOutputThatCantBeHadIsNotOffered()
    {
        Assert.DoesNotThrow(() => _ = SakuraAudioEngine.AAudioCallback);
        Assert.That(AAudioOutput.IsAvailable, Is.False);
    }

    [Test]
    public void TestTheFirstReadingIsTakenAsItIs()
    {
        var latency = new MeasuredLatency();

        Assert.That(latency.Milliseconds, Is.Null);

        latency.Add(30);
        Assert.That(latency.Milliseconds, Is.EqualTo(30));
    }

    /// <summary>
    /// Readings scattered across a callback's width settle on their average, which is the figure that leaves
    /// positions right on average between callbacks.
    /// </summary>
    [Test]
    public void TestReadingsSettleOnTheirAverage()
    {
        var latency = new MeasuredLatency();

        for (int i = 0; i < 200; i++)
            latency.Add(i % 2 == 0 ? 28 : 32);

        Assert.That(latency.Milliseconds, Is.EqualTo(30).Within(0.5));
    }

    [Test]
    public void TestOneStrayReadingIsIgnored()
    {
        var latency = new MeasuredLatency();
        latency.Add(30);

        latency.Add(250);
        latency.Add(30);

        Assert.That(latency.Milliseconds, Is.EqualTo(30).Within(0.001));
    }

    /// <summary>
    /// Bluetooth headphones taking over: the new figure is taken at once rather than crept up on.
    /// </summary>
    [Test]
    public void TestANewOutputIsTakenAtOnce()
    {
        var latency = new MeasuredLatency();
        latency.Add(30);

        latency.Add(220);
        latency.Add(221);
        latency.Add(219);

        Assert.That(latency.Milliseconds, Is.EqualTo(219));
    }

    [TestCase(0)]
    [TestCase(-5)]
    [TestCase(5000)]
    public void TestImpossibleReadingsAreIgnored(double reading)
    {
        var latency = new MeasuredLatency();

        latency.Add(reading);
        Assert.That(latency.Milliseconds, Is.Null);
    }

    [Test]
    public void TestResetForgetsTheFigure()
    {
        var latency = new MeasuredLatency();
        latency.Add(30);

        latency.Reset();
        Assert.That(latency.Milliseconds, Is.Null);

        latency.Add(200);
        Assert.That(latency.Milliseconds, Is.EqualTo(200));
    }
}
