// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// A <see cref="BufferedContainer"/> is an alpha barrier: its subtree renders at full opacity into the
/// offscreen buffer and the fade is applied once, to the flattened composite.
/// Broken in 5dbd158ea83860b8018f1ae4824531633f0d5495.
/// </summary>
[TestFixture]
public class BufferedContainerFadeTest
{
    private ManualClock manual = null!;
    private CachingRoot root = null!;
    private HeadlessAppHost host = null!;

    [OneTimeSetUp]
    public void InitializeLogger() => Logger.Initialize();

    [OneTimeTearDown]
    public void ShutdownLogger() => Logger.Shutdown();

    [SetUp]
    public void SetUp()
    {
        manual = new ManualClock { CurrentTime = 1000 };
        root = new CachingRoot
        {
            Size = new Vector2(800, 600),
            Clock = new FramedClock(manual)
        };

        root.Load();
        root.CompleteLoad();

        // BufferedContainer resolves the host to get at the renderer it releases its framebuffers through,
        // so the subtree needs one cached above it. Nothing here draws, the host is only a dependency.
        host = new HeadlessAppHost("BufferedContainerFadeTest");
        root.CacheHost(host);
    }

    [TearDown]
    public void TearDown() => host.Dispose();

    /// <summary>
    /// A root that can be handed a dependency after loading, which is the only way to satisfy a child's
    /// <c>[BackgroundDependencyLoader]</c> without standing up a whole test app.
    /// </summary>
    private partial class CachingRoot : Container
    {
        public void CacheHost(AppHost value) => Cache(value);
    }

    private void frame()
    {
        manual.CurrentTime += 16;
        root.UpdateSubTree();
    }

    [Test]
    public void APlainContainerCascadesItsFadeToChildren()
    {
        var child = new Box { Size = new Vector2(100, 100) };
        var plain = new Container { Size = new Vector2(200, 200), Alpha = 0.5f };

        plain.Add(child);
        root.Add(plain);

        frame();

        Assert.That(child.DrawAlpha, Is.EqualTo(0.5f).Within(1e-5f), "alpha compounds down an ordinary tree");
    }

    [Test]
    public void ABufferedContainerDoesNotCascadeItsFadeToChildren()
    {
        var child = new Box { Size = new Vector2(100, 100) };
        var buffered = new BufferedContainer { Size = new Vector2(200, 200), Alpha = 0.5f };

        buffered.Add(child);
        root.Add(buffered);

        frame();

        Assert.Multiple(() =>
        {
            Assert.That(child.DrawAlpha, Is.EqualTo(1f).Within(1e-5f), "the child renders opaque into the buffer");
            Assert.That(buffered.DrawAlpha, Is.EqualTo(0.5f).Within(1e-5f), "the container still fades — its composite quad carries it");
        });
    }

    /// <summary>
    /// The barrier stops at the buffered container, so a fade applied further down still behaves normally.
    /// A child's own transparency is part of the image being flattened, not part of the group fade.
    /// </summary>
    [Test]
    public void AChildsOwnAlphaSurvivesTheBarrier()
    {
        var child = new Box { Size = new Vector2(100, 100), Alpha = 0.25f };
        var buffered = new BufferedContainer { Size = new Vector2(200, 200), Alpha = 0.5f };

        buffered.Add(child);
        root.Add(buffered);

        frame();

        Assert.That(child.DrawAlpha, Is.EqualTo(0.25f).Within(1e-5f));
    }

    /// <summary>
    /// The barrier is not a reset to 1 for the whole subtree below it, it only replaces what the buffered
    /// container itself contributes. A grandchild still inherits its own parent's fade.
    /// </summary>
    [Test]
    public void TheBarrierAppliesOnlyToTheBufferedContainersOwnContribution()
    {
        var grandchild = new Box { Size = new Vector2(50, 50) };
        var inner = new Container { Size = new Vector2(100, 100), Alpha = 0.5f };
        var buffered = new BufferedContainer { Size = new Vector2(200, 200), Alpha = 0.5f };

        inner.Add(grandchild);
        buffered.Add(inner);
        root.Add(buffered);

        frame();

        Assert.That(grandchild.DrawAlpha, Is.EqualTo(0.5f).Within(1e-5f), "inner's fade still cascades; only buffered's is deferred");
    }

    /// <summary>
    /// An ancestor's fade above the buffered container reaches the container, and therefore its composite,
    /// but must not leak past the barrier into the subtree.
    /// </summary>
    [Test]
    public void AnAncestorsFadeReachesTheCompositeAndNotTheSubtree()
    {
        var child = new Box { Size = new Vector2(100, 100) };
        var buffered = new BufferedContainer { Size = new Vector2(200, 200), Alpha = 0.5f };
        var outer = new Container { Size = new Vector2(400, 400), Alpha = 0.5f };

        buffered.Add(child);
        outer.Add(buffered);
        root.Add(outer);

        frame();

        Assert.Multiple(() =>
        {
            Assert.That(buffered.DrawAlpha, Is.EqualTo(0.25f).Within(1e-5f), "0.5 outer x 0.5 own");
            Assert.That(child.DrawAlpha, Is.EqualTo(1f).Within(1e-5f));
        });
    }

    /// <summary>
    /// A fully hidden buffered container must still suppress its subtree's work. The barrier makes children
    /// opaque, so the early-out cannot rely on their DrawAlpha and has to come from the
    /// ancestor-hidden flag instead. Test because getting this wrong would make every hidden
    /// buffered container lay out and shape text again.
    /// </summary>
    [Test]
    public void AHiddenBufferedContainerStillHidesItsSubtree()
    {
        var child = new Box { Size = new Vector2(100, 100) };
        var buffered = new BufferedContainer { Size = new Vector2(200, 200), Alpha = 0f };

        buffered.Add(child);
        root.Add(buffered);

        frame();

        Assert.That(child.IsEffectivelyHidden, Is.True, "the child is opaque by design, so this must come from the ancestor flag");
    }
}
