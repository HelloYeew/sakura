// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Pooling;
using Sakura.Framework.Graphics.Textures;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// Tests for the disposal cascade: removing a drawable disposes it and everything below it, unless the
/// remover or the drawable itself opted out.
/// </summary>
[TestFixture]
public class DrawableDisposalTest
{
    [SetUp]
    public void SetUp()
    {
        // Every test here disposes inline so it can assert without pumping frames. Deferral is
        // exercised separately in DrawableDisposalQueueTest.
        DrawableDisposalQueue.Enabled = false;
        DrawableDisposalQueue.Flush();
    }

    [Test]
    public void RemovingADrawableDisposesIt()
    {
        var parent = new Container();
        var child = new TrackedDrawable();

        parent.Add(child);
        parent.Remove(child);

        Assert.Multiple(() =>
        {
            Assert.That(child.IsDisposed, Is.True);
            Assert.That(child.Parent, Is.Null);
        });
    }

    /// <summary>
    /// The whole point of the cascade: the leak was never the removed node, it was what hung below it.
    /// </summary>
    [Test]
    public void DisposalReachesTheWholeSubtree()
    {
        var root = new Container();
        var middle = new Container();
        var leaf = new TrackedDrawable();
        var deepLeaf = new TrackedDrawable();
        var inner = new Container();

        inner.Add(deepLeaf);
        middle.Add(leaf);
        middle.Add(inner);
        root.Add(middle);

        root.Remove(middle);

        Assert.Multiple(() =>
        {
            Assert.That(leaf.IsDisposed, Is.True, "a child of the removed node");
            Assert.That(deepLeaf.IsDisposed, Is.True, "a grandchild — the case OnParentChanged could never see");
            Assert.That(middle.IsDisposed, Is.True);
            Assert.That(root.IsDisposed, Is.False, "the container that did the removing must survive");
        });
    }

    [Test]
    public void ClearDisposesEveryChild()
    {
        var parent = new Container();
        var children = new List<TrackedDrawable>();

        for (int i = 0; i < 5; i++)
        {
            var child = new TrackedDrawable();
            children.Add(child);
            parent.Add(child);
        }

        parent.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(parent.Children, Is.Empty);
            Assert.That(children, Has.All.Matches<TrackedDrawable>(c => c.IsDisposed));
        });
    }

    [Test]
    public void DisposingAContainerDisposesItsChildren()
    {
        var container = new Container();
        var child = new TrackedDrawable();

        container.Add(child);
        container.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(container.IsDisposed, Is.True);
            Assert.That(child.IsDisposed, Is.True);
        });
    }

    [Test]
    public void DisposeIsIdempotent()
    {
        var drawable = new TrackedDrawable();

        drawable.Dispose();
        drawable.Dispose();
        drawable.Dispose();

        Assert.That(drawable.DisposeCallCount, Is.EqualTo(1));
    }

    [Test]
    public void RemovingWithoutDisposalLeavesTheDrawableReusable()
    {
        var oldParent = new Container();
        var newParent = new Container();
        var child = new TrackedDrawable();

        oldParent.Add(child);
        oldParent.Remove(child, false);
        newParent.Add(child);

        Assert.Multiple(() =>
        {
            Assert.That(child.IsDisposed, Is.False);
            Assert.That(child.Parent, Is.EqualTo(newParent));
        });
    }

    [Test]
    public void ClearWithoutDisposalLeavesChildrenReusable()
    {
        var parent = new Container();
        var child = new TrackedDrawable();

        parent.Add(child);
        parent.Clear(false);

        Assert.That(child.IsDisposed, Is.False);
    }

    /// <summary>
    /// <see cref="Drawable.DisposeOnRemoval"/> is the drawable's own standing refusal, so it overrides
    /// the remover's request rather than merely defaulting it.
    /// </summary>
    [Test]
    public void ADrawableCanRefuseRemovalDisposalRegardlessOfTheRemover()
    {
        var parent = new Container();
        var child = new TrackedDrawable { DisposeOnRemoval = false };

        parent.Add(child);
        parent.Remove(child);

        Assert.That(child.IsDisposed, Is.False);
    }

    [Test]
    public void RefusingRemovalDisposalDoesNotBlockAnExplicitDispose()
    {
        var child = new TrackedDrawable { DisposeOnRemoval = false };

        child.Dispose();

        Assert.That(child.IsDisposed, Is.True);
    }

    /// <summary>
    /// The cascade must stop at a child that refuses removal disposal, or tearing down a screen would
    /// destroy pooled drawables the pool is still expecting back.
    /// </summary>
    [Test]
    public void TheCascadeHonoursAChildsRefusal()
    {
        var root = new Container();
        var middle = new Container();
        var keeper = new TrackedDrawable { DisposeOnRemoval = false };
        var ordinary = new TrackedDrawable();

        middle.Add(keeper);
        middle.Add(ordinary);
        root.Add(middle);

        root.Remove(middle);

        Assert.Multiple(() =>
        {
            Assert.That(ordinary.IsDisposed, Is.True);
            Assert.That(keeper.IsDisposed, Is.False);
            Assert.That(keeper.Parent, Is.Null, "it is still detached — only its disposal was refused");
        });
    }

    [Test]
    public void ADisposedDrawableCannotBeAdded()
    {
        var parent = new Container();
        var child = new TrackedDrawable();

        child.Dispose();

        Assert.That(() => parent.Add(child), Throws.InvalidOperationException);
    }

    [Test]
    public void RemoveAllAndRemoveRangeDisposeByDefault()
    {
        var parent = new Container();
        var removedByPredicate = new TrackedDrawable { Name = "match" };
        var removedByRange = new TrackedDrawable();
        var kept = new TrackedDrawable();

        parent.Add(removedByPredicate);
        parent.Add(removedByRange);
        parent.Add(kept);

        parent.RemoveAll(d => d.Name == "match");
        parent.RemoveRange(new[] { removedByRange });

        Assert.Multiple(() =>
        {
            Assert.That(removedByPredicate.IsDisposed, Is.True);
            Assert.That(removedByRange.IsDisposed, Is.True);
            Assert.That(kept.IsDisposed, Is.False);
        });
    }

    [Test]
    public void RemoveAllAndRemoveRangeCanOptOut()
    {
        var parent = new Container();
        var byPredicate = new TrackedDrawable { Name = "match" };
        var byRange = new TrackedDrawable();

        parent.Add(byPredicate);
        parent.Add(byRange);

        parent.RemoveAll(d => d.Name == "match", false);
        parent.RemoveRange(new[] { byRange }, false);

        Assert.Multiple(() =>
        {
            Assert.That(byPredicate.IsDisposed, Is.False);
            Assert.That(byRange.IsDisposed, Is.False);
        });
    }

    /// <summary>
    /// Assigning <see cref="Container.Children"/> clears first, so the drawables being replaced are
    /// disposed — the same rule as an explicit <see cref="Container.Clear(bool)"/>.
    /// </summary>
    [Test]
    public void ReplacingChildrenDisposesTheOldOnes()
    {
        var parent = new Container();
        var original = new TrackedDrawable();

        parent.Add(original);
        parent.Children = new Drawable[] { new TrackedDrawable() };

        Assert.That(original.IsDisposed, Is.True);
    }

    #region Pooling

    /// <summary>
    /// A pooled drawable is removed from its parent precisely in order to be reused, so removal must
    /// return it to its pool without disposing it.
    /// </summary>
    [Test]
    public void APooledDrawableSurvivesRemoval()
    {
        var pool = new DrawablePool<PooledTestDrawable>(1);
        var parent = new Container();

        pool.Load();

        var pooled = pool.Get();
        parent.Add(pooled);
        parent.Remove(pooled);

        Assert.Multiple(() =>
        {
            Assert.That(pooled.IsDisposed, Is.False);
            Assert.That(pool.CountAvailable, Is.EqualTo(1), "it should be back in the pool");
        });
    }

    /// <summary>
    /// The teardown case: the container holding a checked-out pooled drawable is disposed. The drawable
    /// must go back to its pool rather than being destroyed on the way down.
    /// </summary>
    [Test]
    public void APooledDrawableSurvivesItsHoldersDisposal()
    {
        var pool = new DrawablePool<PooledTestDrawable>(1);
        var holder = new Container();

        pool.Load();

        var pooled = pool.Get();
        holder.Add(pooled);
        holder.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(pooled.IsDisposed, Is.False);
            Assert.That(pool.CountAvailable, Is.EqualTo(1));
        });
    }

    [Test]
    public void DisposingAPoolDisposesWhatItHolds()
    {
        var pool = new DrawablePool<PooledTestDrawable>(2);

        pool.Load();
        var pooledDrawables = new List<PooledTestDrawable>();

        for (int i = 0; i < 2; i++)
            pooledDrawables.Add(pool.Get());

        foreach (var d in pooledDrawables)
            d.Return();

        pool.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(pooledDrawables, Has.All.Matches<PooledTestDrawable>(d => d.IsDisposed));
            Assert.That(pool.CountAvailable, Is.Zero);
        });
    }

    /// <summary>
    /// Sibling disposal order is not something a caller controls, so a drawable can come back after its
    /// pool has already gone. Pushing it onto a disposed pool's stack would simply lose it.
    /// </summary>
    [Test]
    public void ReturningToADisposedPoolDisposesTheDrawable()
    {
        var pool = new DrawablePool<PooledTestDrawable>(1);

        pool.Load();

        var pooled = pool.Get();
        pool.Dispose();

        pooled.Return();

        Assert.That(pooled.IsDisposed, Is.True);
    }

    #endregion

    /// <summary>
    /// A <see cref="GridContainer"/> re-layout throws its own cell containers away and re-adds the
    /// caller's content into fresh ones, so clearing the old cells has to opt out of disposal.
    /// </summary>
    /// <remarks>
    /// Only observable with deferral off. With the queue in play the re-add lands in the same call and
    /// cancels the pending disposal, which hides the bug rather than fixing it — the grid would still
    /// destroy the caller's content anywhere the queue is not being pumped.
    /// </remarks>
    [Test]
    public void AGridRelayoutKeepsTheCallersContent()
    {
        var grid = new GridContainer
        {
            RowDimensions = new[] { new Dimension(GridSizeMode.Distributed) },
            ColumnDimensions = new[] { new Dimension(GridSizeMode.Distributed) }
        };
        var content = new TrackedDrawable();

        grid.Load();
        grid.CompleteLoad();

        grid.Content = new Drawable?[][] { new Drawable?[] { content } };
        grid.Update();

        // Same content, new dimensions: a full re-layout over drawables that must survive it.
        grid.RowDimensions = new[] { new Dimension(GridSizeMode.Absolute, 120) };
        grid.Content = new Drawable?[][] { new Drawable?[] { content } };
        grid.Update();

        Assert.Multiple(() =>
        {
            Assert.That(content.IsDisposed, Is.False);
            Assert.That(content.Parent, Is.Not.Null, "it should be back in a freshly built cell");
        });
    }

    /// <summary>
    /// The acceptance case, end to end: a resource-owning drawable nested below a removed node releases
    /// its GPU texture, and the live-texture accounting returns to where it started.
    /// </summary>
    [Test]
    public void RemovingASubtreeReleasesResourcesOwnedBelowIt()
    {
        TextureRegistry.Reset();

        var root = new Container();
        var screen = new Container();
        var innerLayer = new Container();
        var coverOwner = new TextureOwningDrawable();

        innerLayer.Add(coverOwner);
        screen.Add(innerLayer);
        root.Add(screen);

        long baselineBytes = 0;
        Assert.Multiple(() =>
        {
            Assert.That(TextureRegistry.LiveCount, Is.EqualTo(1), "the cover texture is live while the subtree is");
            Assert.That(TextureRegistry.LiveBytes, Is.GreaterThan(baselineBytes));
        });

        root.Remove(screen);

        Assert.Multiple(() =>
        {
            Assert.That(coverOwner.IsDisposed, Is.True);
            Assert.That(TextureRegistry.LiveCount, Is.Zero, "live count must return to its baseline");
            Assert.That(TextureRegistry.LiveBytes, Is.EqualTo(baselineBytes));
        });

        TextureRegistry.Reset();
    }

    private partial class TrackedDrawable : Container
    {
        public int DisposeCallCount { get; private set; }

        protected override void Dispose(bool isDisposing)
        {
            if (!IsDisposed)
                DisposeCallCount++;

            base.Dispose(isDisposing);
        }
    }

    private partial class PooledTestDrawable : PoolableDrawable
    {
    }

    /// <summary>
    /// The shape a game's cover/background drawable takes: it owns its texture, so its disposal is the
    /// only thing that releases it.
    /// </summary>
    private partial class TextureOwningDrawable : Container
    {
        public TextureOwningDrawable()
        {
            Texture = new Texture(new HeadlessNativeTexture(1920, 1080)) { Name = "beatmap-background-1" };
        }

        protected override void Dispose(bool isDisposing)
        {
            Texture?.Dispose();
            Texture = null;

            base.Dispose(isDisposing);
        }
    }
}
