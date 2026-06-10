// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering;

namespace Sakura.Framework.Tests.Rendering;

/// <summary>
/// Tests for the triple-buffer handoff between the update and draw loops.
/// The invariants here are what guarantee the draw thread only ever sees complete,
/// fully-updated frames — the other half of the stale-position defenses alongside
/// <see cref="DrawNodeSnapshotTest"/>.
/// Related fix : https://github.com/HelloYeew/sakura/pull/104
/// </summary>
[TestFixture]
public class FrameBufferManagerTest
{
    [Test]
    public void TestDrawReceivesJustFinishedBuffer()
    {
        var manager = new FrameBufferManager();

        int updateIndex = manager.GetUpdateIndex();
        manager.FinishUpdate();

        Assert.That(manager.GetDrawIndex(), Is.EqualTo(updateIndex), "Draw should receive the buffer the update loop just finished");
    }

    [Test]
    public void TestDrawIndexIsStableWhenNoNewFrameIsReady()
    {
        var manager = new FrameBufferManager();

        manager.GetUpdateIndex();
        manager.FinishUpdate();

        int first = manager.GetDrawIndex();
        int second = manager.GetDrawIndex(); // draw runs again before the next update finishes

        Assert.That(second, Is.EqualTo(first), "Without a new finished frame, draw must keep rendering the same buffer");
    }

    [Test]
    public void TestDrawSkipsToLatestWhenUpdateRunsAhead()
    {
        var manager = new FrameBufferManager();

        manager.GetUpdateIndex();
        manager.FinishUpdate(); // frame A (never drawn)

        int second = manager.GetUpdateIndex();
        manager.FinishUpdate(); // frame B

        Assert.That(manager.GetDrawIndex(), Is.EqualTo(second), "When update outpaces draw, draw should receive the newest finished frame");
    }

    [Test]
    public void TestUpdateNeverWritesBufferHeldByDraw()
    {
        var manager = new FrameBufferManager();

        // Interleave update-heavy and draw-heavy phases and verify the update loop is
        // never handed the buffer the draw loop is currently holding. If this invariant
        // broke, the draw thread would render half-written vertex data — torn frames.
        int drawIndex = manager.GetDrawIndex();

        for (int i = 0; i < 200; i++)
        {
            int updates = i % 3 + 1; // 1..3 updates per draw
            for (int u = 0; u < updates; u++)
            {
                int updateIndex = manager.GetUpdateIndex();
                Assert.That(updateIndex, Is.Not.EqualTo(drawIndex), $"Update was handed the draw buffer at iteration {i}");
                manager.FinishUpdate();
            }

            drawIndex = manager.GetDrawIndex();
        }
    }

    [Test]
    public void TestIndicesAlwaysFormValidTripleBuffer()
    {
        var manager = new FrameBufferManager();

        for (int i = 0; i < 100; i++)
        {
            int updateIndex = manager.GetUpdateIndex();
            manager.FinishUpdate();
            int drawIndex = manager.GetDrawIndex();

            Assert.Multiple(() =>
            {
                Assert.That(updateIndex, Is.InRange(0, 2));
                Assert.That(drawIndex, Is.InRange(0, 2));
            });
        }
    }
}
