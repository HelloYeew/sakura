// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Diagnostics;
using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// Where the time goes between an update and its frame being on screen.
/// </summary>
[TestFixture]
public class DisplayLatencySplitTest
{
    /// <summary>
    /// Milliseconds as a <see cref="Stopwatch"/> timestamp, from an arbitrary start.
    /// </summary>
    private static long at(double milliseconds) => 1_000_000 + (long)(milliseconds * Stopwatch.Frequency / 1000);

    /// <summary>
    /// A frame from the update that began at <paramref name="content"/>, on screen at <paramref name="shownAt"/>.
    /// </summary>
    private static ShownFrame shown(long content, long shownAt, long gpuDone = 0) => new ShownFrame(content, gpuDone, shownAt);

    [Test]
    public void TestAFrameIsSplitIntoWhereTheTimeWent()
    {
        var split = new DisplayLatencySplit();
        split.AddDrawn(at(0), at(2), at(5), at(9));
        split.AddShown(shown(at(0), at(30)));

        var parts = split.Milliseconds;

        Assert.That(parts.Update, Is.EqualTo(2).Within(0.01));
        Assert.That(parts.Waiting, Is.EqualTo(3).Within(0.01));
        Assert.That(parts.Draw, Is.EqualTo(4).Within(0.01));
        Assert.That(parts.Gpu, Is.EqualTo(0), "the renderer didn't say");
        Assert.That(parts.Presenting, Is.EqualTo(21).Within(0.01));
    }

    /// <summary>
    /// Each part settles on its average, so they keep adding up to the whole.
    /// </summary>
    [Test]
    public void TestPartsSettleOnTheirAverage()
    {
        var split = new DisplayLatencySplit();

        for (int i = 0; i < 400; i++)
        {
            double start = i * 10;
            double shownAfter = i % 2 == 0 ? 20 : 30;
            split.AddDrawn(at(start), at(start + 2), at(start + 4), at(start + 6));
            split.AddShown(shown(at(start), at(start + 6 + shownAfter)));
        }

        var parts = split.Milliseconds;

        Assert.That(parts.Presenting, Is.EqualTo(25).Within(1));
        Assert.That(parts.Update + parts.Waiting + parts.Draw, Is.EqualTo(6).Within(0.01));
    }

    /// <summary>
    /// A frame the host didn't draw lately, or one shown before it was handed over, says nothing.
    /// </summary>
    [Test]
    public void TestUnmatchedFramesAreIgnored()
    {
        var split = new DisplayLatencySplit();
        split.AddDrawn(at(0), at(2), at(5), at(9));
        split.AddShown(shown(at(100), at(130)));
        split.AddShown(shown(at(0), at(8)));

        var parts = split.Milliseconds;

        Assert.That(parts.Update + parts.Waiting + parts.Draw + parts.Gpu + parts.Presenting, Is.EqualTo(0));
    }

    /// <summary>
    /// An update drawn twice, when no newer one was ready, is matched to the draw handed over before it was shown.
    /// </summary>
    [Test]
    public void TestARepeatedUpdateMatchesTheDrawBeforeItWasShown()
    {
        var split = new DisplayLatencySplit();
        split.AddDrawn(at(0), at(2), at(5), at(9));
        split.AddDrawn(at(0), at(2), at(20), at(25));
        split.AddShown(shown(at(0), at(22)));

        Assert.That(split.Milliseconds.Draw, Is.EqualTo(4).Within(0.01));
    }

    /// <summary>
    /// Where the renderer says when the GPU finished, the time after the draw splits into the GPU and presenting.
    /// </summary>
    [Test]
    public void TestTheGpusTimeIsSplitOffWhereItsKnown()
    {
        var split = new DisplayLatencySplit();
        split.AddDrawn(at(0), at(2), at(5), at(9));
        split.AddShown(shown(at(0), at(30), at(12)));

        var parts = split.Milliseconds;

        Assert.That(parts.Gpu, Is.EqualTo(3).Within(0.01));
        Assert.That(parts.Presenting, Is.EqualTo(18).Within(0.01));
    }
}
