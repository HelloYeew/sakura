// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// <see cref="TextureBindCounter"/> reports the last <em>completed</em> frame, from counters that roll
/// themselves forward lazily rather than being swept at each frame boundary.
/// </summary>
[TestFixture]
public class TextureBindTrackerTest
{
    private TextureBindCounter counter = null!;

    [SetUp]
    public void SetUp()
    {
        counter = new TextureBindCounter();

        // Other fixtures draw through the headless renderer and share the process-wide frame index, so
        // start from a known boundary rather than assuming one.
        TextureBindTracker.EndFrame();
    }

    private static void record(TextureBindCounter target, int times)
    {
        for (int i = 0; i < times; i++)
            target.Record();
    }

    [Test]
    public void ACounterStartsAtZero()
    {
        Assert.That(counter.LastFrame, Is.Zero);
    }

    /// <summary>
    /// Binds are only reported once their frame has closed, so the number a reader sees never changes
    /// under it mid-frame.
    /// </summary>
    [Test]
    public void BindsAreReportedOnlyOnceTheirFrameIsComplete()
    {
        record(counter, 3);
        Assert.That(counter.LastFrame, Is.Zero, "the frame is still in progress");

        TextureBindTracker.EndFrame();
        Assert.That(counter.LastFrame, Is.EqualTo(3));
    }

    /// <summary>
    /// The reading that costs nothing: a texture that stops being bound reports its last frame while that
    /// frame is the previous one, then zero, without anything having to visit it.
    /// </summary>
    [Test]
    public void ACounterThatStopsBeingBoundFallsToZero()
    {
        record(counter, 2);
        TextureBindTracker.EndFrame();

        Assert.That(counter.LastFrame, Is.EqualTo(2));

        TextureBindTracker.EndFrame();
        Assert.That(counter.LastFrame, Is.Zero, "a whole frame passed without a bind");

        TextureBindTracker.EndFrame();
        Assert.That(counter.LastFrame, Is.Zero, "and it stays there");
    }

    /// <summary>
    /// The case the roll-forward exists for: the first bind of a new frame must move the completed total
    /// aside rather than adding to it, so a reader that arrives mid-frame still sees the finished figure.
    /// </summary>
    [Test]
    public void RebindingInANewFrameKeepsTheCompletedFrameReadable()
    {
        record(counter, 4);
        TextureBindTracker.EndFrame();

        record(counter, 1);
        Assert.That(counter.LastFrame, Is.EqualTo(4), "the new frame's bind must not leak into the completed total");

        record(counter, 6);
        Assert.That(counter.LastFrame, Is.EqualTo(4));

        TextureBindTracker.EndFrame();
        Assert.That(counter.LastFrame, Is.EqualTo(7));
    }

    /// <summary>
    /// A counter that sat out several frames and then gets bound again reports zero for the frame before,
    /// not the stale total from whenever it was last drawn.
    /// </summary>
    [Test]
    public void ReturningAfterAGapDoesNotResurrectTheOldTotal()
    {
        record(counter, 5);
        TextureBindTracker.EndFrame();
        TextureBindTracker.EndFrame();
        TextureBindTracker.EndFrame();

        record(counter, 1);
        Assert.That(counter.LastFrame, Is.Zero);
    }

    /// <summary>
    /// The frame total is the sum over every texture, and is published on the same boundary.
    /// </summary>
    [Test]
    public void TheFrameTotalSumsEveryCounter()
    {
        var other = new TextureBindCounter();

        record(counter, 2);
        record(other, 3);

        TextureBindTracker.EndFrame();

        Assert.Multiple(() =>
        {
            Assert.That(GlobalStatistics.Get<int>("Renderer", "Texture Binds (Last Frame)").Value, Is.EqualTo(5));
            Assert.That(counter.LastFrame, Is.EqualTo(2));
            Assert.That(other.LastFrame, Is.EqualTo(3));
        });

        TextureBindTracker.EndFrame();

        Assert.That(GlobalStatistics.Get<int>("Renderer", "Texture Binds (Last Frame)").Value, Is.Zero, "the total resets with the frame");
    }
}
