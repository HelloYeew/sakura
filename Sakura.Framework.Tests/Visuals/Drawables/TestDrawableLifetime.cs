// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
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

        AddStep("Add disposable expiring drawable", () =>
        {
            Clear();
            drawable = new LifetimeTestDrawable
            {
                Text = "I will dispose on death",
                DisposeOnRemoval = true
            };
            Add(drawable);
            drawable.Expire();
        });

        AddAssert("Drawable is disposed", () => drawable.IsDisposed);
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
    private partial class LifetimeTestDrawable : Container, IDisposable
    {
        public bool IsDisposed { get; private set; }
        public int UpdateCount { get; private set; }

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

            Add(new Box
            {
                RelativeSizeAxes = Axes.Both,
                Color = Color.DarkSlateBlue
            });

            Add(spriteText = new SpriteText
            {
                Text = "Lifetime Test",
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Color = Color.White
            });
        }

        public override void Update()
        {
            base.Update();
            UpdateCount++;
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
