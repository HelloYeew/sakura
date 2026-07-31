// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Drawables;

public partial class TestDrawableLifetime : TestScene
{
    [Test]
    public void TestFutureLifetimeStart()
    {
        LifetimeTestDrawable drawable = null!;

        AddStep("Spawn drawable in the future", () =>
        {
            Clear();
            drawable = new LifetimeTestDrawable
            {
                LifetimeStart = Clock.CurrentTime + 500,
                Text = "Waiting to spawn..."
            };
            Add(drawable);
        });

        AddAssert("Drawable is not alive yet", () => !drawable.IsAlive);
        AddAssert("Update count is 0", () => drawable.UpdateCount == 0);

        AddWaitStep("Wait 500ms for spawn", 500);

        AddStep("Update text", () => drawable.Text = "Spawned!");
        AddUntilStep("Drawable is now alive", () => drawable.IsAlive);
        AddUntilStep("Drawable is updating", () => drawable.UpdateCount > 0);
    }

    [Test]
    public void TestExpiryAndRemoval()
    {
        LifetimeTestDrawable drawable = null!;

        AddStep("Add expiring drawable", () =>
        {
            Clear();
            drawable = new LifetimeTestDrawable
            {
                Text = "I will expire instantly"
            };
            Add(drawable);

            drawable.Expire();
        });

        AddAssert("Drawable is dead", () => !drawable.IsAlive);
        AddAssert("Drawable is removed from parent", () => drawable.Parent == null && !Contains(drawable));
    }

    [Test]
    public void TestDisposeOnRemoval()
    {
        LifetimeTestDrawable drawable = null!;

        AddStep("Add expiring drawable", () =>
        {
            Clear();
            drawable = new LifetimeTestDrawable
            {
                Text = "I will dispose on death"
            };
            Add(drawable);
            drawable.Expire();
        });

        // AddUntilStep, not AddAssert: removal-triggered disposal is budgeted and may land a frame later
        // than the removal itself (see DrawableDisposalQueue).
        AddUntilStep("Drawable is disposed", () => drawable.IsDisposed);
        AddUntilStep("Cascade reached its children", () => drawable.TrackedChildren.TrueForAll(c => c.IsDisposed));
    }

    [Test]
    public void TestRemovalWithoutDisposal()
    {
        LifetimeTestDrawable drawable = null!;

        AddStep("Add drawable that opts out of removal disposal", () =>
        {
            Clear();
            drawable = new LifetimeTestDrawable
            {
                Text = "I outlive my removal",
                DisposeOnRemoval = false
            };
            Add(drawable);
            drawable.Expire();
        });

        AddUntilStep("Drawable was removed", () => drawable.Parent == null);
        AddAssert("Drawable is not disposed", () => !drawable.IsDisposed);
        AddAssert("Nor are its children", () => drawable.TrackedChildren.TrueForAll(c => !c.IsDisposed));
        AddStep("Re-add the same drawable", () =>
        {
            // It was removed for being dead, so revive it first or it is removed again immediately.
            drawable.LifetimeEnd = double.MaxValue;
            Add(drawable);
        });
        AddAssert("Drawable is in the tree again", () => Contains(drawable));
    }

    [Test]
    public void TestKeepAliveAfterDeath()
    {
        LifetimeTestDrawable drawable = null!;

        AddStep("Add persistent dead drawable", () =>
        {
            Clear();
            drawable = new LifetimeTestDrawable
            {
                Text = "I am dead, but I remain.",
                RemoveWhenNotAlive = false
            };
            Add(drawable);
            drawable.Expire();
        });

        AddAssert("Drawable is dead", () => !drawable.IsAlive);
        AddAssert("Drawable is STILL in parent", () => drawable.Parent != null && Contains(drawable));

        AddStep("Force manual removal", () => Remove(drawable));
        AddAssert("Drawable is finally removed", () => drawable.Parent == null);
    }

    /// <summary>
    /// A dummy drawable that tracks its own updates and disposal state for testing.
    /// </summary>
    private partial class LifetimeTestDrawable : Container
    {
        public int UpdateCount { get; private set; }

        /// <summary>
        /// This drawable's own children, held separately so a test can still see them after disposal has
        /// detached them — which is how the cascade reaching below the removed node is observable.
        /// </summary>
        public readonly List<Drawable> TrackedChildren = new List<Drawable>();

        private readonly SpriteText spriteText;

        public string Text
        {
            get => spriteText.Text;
            set => spriteText.Text = value;
        }

        public LifetimeTestDrawable()
        {
            Size = new Vector2(250, 50);
            Anchor = Anchor.Centre;
            Origin = Anchor.Centre;

            var box = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Color = Color.DarkSlateBlue
            };

            Add(box);
            TrackedChildren.Add(box);

            Add(spriteText = new SpriteText
            {
                Text = "Lifetime Test",
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Color = Color.White
            });

            TrackedChildren.Add(spriteText);
        }

        public override void Update()
        {
            base.Update();
            UpdateCount++;
        }


    }
}
