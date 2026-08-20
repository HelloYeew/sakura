// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Drawables;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// Regression test of <see cref="Drawable.LoadComplete"/> override is where consumers put one-time set up an event
/// subscription, a reactive binding, spawning a child so it must run exactly once per drawable.
/// </summary>
/// TODO: Add PR link here
[TestFixture]
public class LoadCompleteOnceTest
{
    private partial class CountingContainer : Container
    {
        public int LoadCompleteCalls;

        protected override void LoadComplete()
        {
            base.LoadComplete();
            LoadCompleteCalls++;
        }
    }

    [Test]
    public void ChildAddedDuringParentLoadCompletesOnce()
    {
        var child = new CountingContainer();
        var parent = new Container
        {
            Child = child
        };

        parent.Load();
        parent.CompleteLoad();

        Assert.That(child.LoadCompleteCalls, Is.EqualTo(1));
    }

    /// <summary>
    /// The guarantee itself: completing a drawable that is already loaded does nothing, so no caller
    /// can make its override run twice.
    /// </summary>
    [Test]
    public void RepeatedCompleteLoadRunsOverrideOnce()
    {
        var drawable = new CountingContainer();

        drawable.Load();
        drawable.CompleteLoad();
        drawable.CompleteLoad();
        drawable.CompleteLoad();

        Assert.That(drawable.LoadCompleteCalls, Is.EqualTo(1));
    }

    /// <summary>
    /// The regression case. A child added to an already-loaded container is loaded on the spot by
    /// AddInternal, the next cascade from an ancestor must not complete it a second time.
    /// </summary>
    [Test]
    public void ChildAddedAfterParentLoadedCompletesOnce()
    {
        var parent = new Container();
        parent.Load();
        parent.CompleteLoad();

        var child = new CountingContainer();
        parent.Add(child);

        Assert.That(child.LoadCompleteCalls, Is.EqualTo(1), "AddInternal should have completed the child exactly once.");

        // What a grandparent's cascade does on the next pass.
        parent.CompleteLoad();

        Assert.That(child.LoadCompleteCalls, Is.EqualTo(1), "A repeat cascade must not re-run the child's override.");
    }

    /// <summary>
    /// a container loaded during its parent's load pass, holding a child
    /// added from inside that pass i.e. App.Load adding to a container that is already loaded,
    /// followed by the host's own LoadComplete cascade over the whole tree.
    /// </summary>
    [Test]
    public void GrandchildAddedDuringLoadCompletesOnceUnderFullCascade()
    {
        var grandchild = new CountingContainer();
        var middle = new Container();
        var root = new Container
        {
            Child = middle
        };

        root.Load();
        middle.Add(grandchild);
        root.CompleteLoad();

        Assert.That(grandchild.LoadCompleteCalls, Is.EqualTo(1));
    }
}
