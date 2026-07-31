// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Graphics.Drawables;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// Tests for <see cref="DrawableDisposalQueue"/>
/// </summary>
[TestFixture]
public class DrawableDisposalQueueTest
{
    private bool wasEnabled;

    [SetUp]
    public void SetUp()
    {
        wasEnabled = DrawableDisposalQueue.Enabled;
        DrawableDisposalQueue.Flush();
        DrawableDisposalQueue.Enabled = true;
    }

    [TearDown]
    public void TearDown()
    {
        DrawableDisposalQueue.Flush();
        DrawableDisposalQueue.Enabled = wasEnabled;
        DrawableDisposalQueue.ItemsPerFrameBudget = DrawableDisposalQueue.DEFAULT_ITEMS_PER_FRAME;
    }

    [Test]
    public void RemovalDefersDisposalUntilTheQueueIsProcessed()
    {
        var parent = new Container();
        var child = new Container();

        parent.Add(child);
        parent.Remove(child);

        Assert.Multiple(() =>
        {
            Assert.That(child.IsDisposed, Is.False, "disposal should be queued, not inline");
            Assert.That(child.Parent, Is.Null, "detachment is immediate either way");
            Assert.That(DrawableDisposalQueue.PendingCount, Is.EqualTo(1));
        });

        DrawableDisposalQueue.Process();

        Assert.That(child.IsDisposed, Is.True);
    }

    /// <summary>
    /// Without a loop to drain it, a queued drawable would never be disposed at all — so with deferral
    /// off, removal has to dispose inline.
    /// </summary>
    [Test]
    public void DisposalIsInlineWhileDeferralIsDisabled()
    {
        DrawableDisposalQueue.Enabled = false;

        var parent = new Container();
        var child = new Container();

        parent.Add(child);
        parent.Remove(child);

        Assert.That(child.IsDisposed, Is.True);
    }

    /// <summary>
    /// A container's cascade queues its children rather than recursing, which is what makes the budget
    /// bound the frame: each queued item is a bounded amount of work, so disposal walks a deep tree
    /// breadth-first over as many frames as it needs.
    /// </summary>
    [Test]
    public void TheCascadeIsBreadthFirstAcrossFrames()
    {
        var root = new Container();
        var level1 = new Container();
        var level2 = new Container();
        var level3 = new Container();

        level2.Add(level3);
        level1.Add(level2);
        root.Add(level1);

        root.Remove(level1);

        DrawableDisposalQueue.Process();
        Assert.Multiple(() =>
        {
            Assert.That(level1.IsDisposed, Is.True);
            Assert.That(level2.IsDisposed, Is.True, "queued by level 1's disposal and drained in the same pass");
            Assert.That(level3.IsDisposed, Is.True);
        });
    }

    [Test]
    public void ProcessStopsAtTheFrameBudget()
    {
        var parent = new Container();
        var children = new List<Container>();

        for (int i = 0; i < 10; i++)
        {
            var child = new Container();
            children.Add(child);
            parent.Add(child);
        }

        parent.Clear();

        DrawableDisposalQueue.ItemsPerFrameBudget = 4;

        Assert.That(DrawableDisposalQueue.Process(), Is.EqualTo(4));
        Assert.That(DrawableDisposalQueue.PendingCount, Is.EqualTo(6));

        Assert.That(DrawableDisposalQueue.Process(), Is.EqualTo(4));
        Assert.That(DrawableDisposalQueue.Process(), Is.EqualTo(2));

        Assert.Multiple(() =>
        {
            Assert.That(DrawableDisposalQueue.PendingCount, Is.Zero);
            Assert.That(children, Has.All.Matches<Container>(c => c.IsDisposed));
        });
    }

    /// <summary>
    /// Removing and re-adding within the same frame is a misuse of the default (the caller should have
    /// passed <c>dispose: false</c>), but it must not hand back a drawable that dies a frame later.
    /// </summary>
    [Test]
    public void ReAddingBeforeTheQueueDrainsCancelsTheDisposal()
    {
        var oldParent = new Container();
        var newParent = new Container();
        var child = new Container();

        oldParent.Add(child);
        oldParent.Remove(child);
        newParent.Add(child);

        DrawableDisposalQueue.Process();

        Assert.Multiple(() =>
        {
            Assert.That(child.IsDisposed, Is.False);
            Assert.That(child.Parent, Is.EqualTo(newParent));
        });
    }

    [Test]
    public void FlushIgnoresTheBudget()
    {
        var parent = new Container();

        for (int i = 0; i < 20; i++)
            parent.Add(new Container());

        parent.Clear();

        DrawableDisposalQueue.ItemsPerFrameBudget = 1;

        Assert.That(DrawableDisposalQueue.Flush(), Is.EqualTo(20));
        Assert.That(DrawableDisposalQueue.PendingCount, Is.Zero);
    }
}
