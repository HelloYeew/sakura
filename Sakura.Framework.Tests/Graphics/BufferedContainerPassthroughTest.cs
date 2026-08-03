// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// A <see cref="BufferedContainer"/> that would composite at neutral color with no effect asked for
/// skips the offscreen pass entirely and draws its subtree straight to the target.
/// </summary>
[TestFixture]
public class BufferedContainerPassthroughTest
{
    private const int width = 80;
    private const int height = 40;

    // Opaque, and primary so that sRGB-to-linear conversion is exact (0 and 255 map to 0 and 1).
    private static readonly Color lower = Color.Red;
    private static readonly Color upper = Color.Lime;

    private static readonly Vector4 background = new Vector4(0, 0, 1, 1);

    // Only the lower box covers this column, both cover the other.
    private const int lower_only_x = 10;
    private const int overlap_x = 40;
    private const int sample_y = 20;

    private ManualClock manual = null!;
    private CachingRoot root = null!;
    private HeadlessAppHost host = null!;
    private HeadlessRenderer renderer = null!;

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
            Size = new Vector2(width, height),
            Clock = new FramedClock(manual)
        };

        root.Load();
        root.LoadComplete();

        host = new HeadlessAppHost(nameof(BufferedContainerPassthroughTest));
        root.CacheHost(host);

        renderer = new HeadlessRenderer(new HeadlessTextureManager());
        renderer.EnablePixelCapture(width, height);
    }

    [TearDown]
    public void TearDown() => host.Dispose();

    private partial class CachingRoot : Container
    {
        public void CacheHost(AppHost value) => Cache(value);
    }

    private PixelSurface render()
    {
        manual.CurrentTime += 16;
        root.UpdateSubTree();

        renderer.Screen.Clear(background);
        renderer.SetRoot(root.GenerateDrawNodeSubtree(0));
        renderer.Draw(new FramedClock(manual));

        return renderer.Screen;
    }

    private static void addOverlappingBoxes(Container target)
    {
        target.Add(new Box { Position = new Vector2(0, 0), Size = new Vector2(60, height), Color = lower });
        target.Add(new Box { Position = new Vector2(20, 0), Size = new Vector2(60, height), Color = upper });
    }

    /// <summary>
    /// Renders one frame of a buffered container in the given state and reports whether it took the
    /// offscreen path. A framebuffer is only ever created by the buffered branch, so its presence after a
    /// frame is exactly the branch that ran.
    /// </summary>
    private bool buffers(BufferedContainer buffered)
    {
        root.Add(buffered);
        render();

        return buffered.SharedData.FrameBuffer != null;
    }

    private static BufferedContainer neutral() => new BufferedContainer { Size = new Vector2(width, height) };

    /// <summary>
    /// The baseline: nothing asked for, nothing tinted, so nothing is bought by the buffer.
    /// </summary>
    [Test]
    public void ANeutralBufferedContainerSkipsTheBuffer()
    {
        var buffered = neutral();
        addOverlappingBoxes(buffered);

        Assert.That(buffers(buffered), Is.False, "no effect, no tint, no fade — the offscreen pass buys nothing");
    }

    /// <summary>
    /// And skipping it changes no pixels, including in the overlap region, which is where a composite that
    /// had lost or double-applied anything would show it. This is the equivalence the whole item rests on.
    /// </summary>
    [Test]
    public void PassthroughIsPixelIdenticalToAPlainContainer()
    {
        var buffered = neutral();
        addOverlappingBoxes(buffered);
        root.Add(buffered);

        var bufferedSurface = render();
        var passthroughOverlap = bufferedSurface[overlap_x, sample_y];
        var passthroughLowerOnly = bufferedSurface[lower_only_x, sample_y];

        Assert.That(buffered.SharedData.FrameBuffer, Is.Null, "the state under test must actually be the passthrough one");

        SetUp();

        var plain = new Container { Size = new Vector2(width, height) };
        addOverlappingBoxes(plain);
        root.Add(plain);

        var plainSurface = render();
        var plainOverlap = plainSurface[overlap_x, sample_y];
        var plainLowerOnly = plainSurface[lower_only_x, sample_y];

        Assert.Multiple(() =>
        {
            Assert.That(passthroughOverlap.X, Is.EqualTo(plainOverlap.X).Within(1e-4f), "overlap: red");
            Assert.That(passthroughOverlap.Y, Is.EqualTo(plainOverlap.Y).Within(1e-4f), "overlap: green");
            Assert.That(passthroughOverlap.Z, Is.EqualTo(plainOverlap.Z).Within(1e-4f), "overlap: blue");

            Assert.That(passthroughLowerOnly.X, Is.EqualTo(plainLowerOnly.X).Within(1e-4f), "lower box only: red");
            Assert.That(passthroughLowerOnly.Y, Is.EqualTo(plainLowerOnly.Y).Within(1e-4f), "lower box only: green");
            Assert.That(passthroughLowerOnly.Z, Is.EqualTo(plainLowerOnly.Z).Within(1e-4f), "lower box only: blue");
        });
    }

    #region The colour identity check

    /// <summary>
    /// A fade must buffer: the container is an alpha barrier, so a faded one fades one flattened image,
    /// and a passthrough would fade each child instead and let the overlap bleed through.
    /// </summary>
    [Test]
    public void AFadeForcesTheBuffer()
    {
        var buffered = neutral();
        buffered.Alpha = 0.5f;
        addOverlappingBoxes(buffered);

        Assert.That(buffers(buffered), Is.True);
    }

    /// <summary>
    /// A tint must buffer: colour does not cascade to children in this framework, so it can only be
    /// applied on the composite quad — a passthrough would drop it entirely.
    /// </summary>
    [Test]
    public void ATintForcesTheBuffer()
    {
        var buffered = neutral();
        buffered.Color = Color.Red;
        addOverlappingBoxes(buffered);

        Assert.That(buffers(buffered), Is.True);
    }

    /// <summary>
    /// The channel a three-clause predicate would have missed. <see cref="Color.A"/> is folded into the
    /// vertex colour's alpha independently of <see cref="Drawable.DrawAlpha"/>, and
    /// <see cref="BufferedContainer.ChildDrawAlpha"/> does not carry it either — so a passthrough here
    /// would render the subtree fully opaque.
    /// </summary>
    [Test]
    public void AColorAlphaBelowFullForcesTheBuffer()
    {
        var buffered = neutral();
        buffered.Color = Color.FromArgb(128, Color.White);
        addOverlappingBoxes(buffered);

        bool tookTheBuffer = buffers(buffered);

        Assert.Multiple(() =>
        {
            Assert.That(buffered.DrawAlpha, Is.EqualTo(1f).Within(1e-5f), "this is not a fade — DrawAlpha is untouched");
            Assert.That(tookTheBuffer, Is.True, "and it still has to buffer");
        });
    }

    /// <summary>
    /// Checking all four corners rather than just the first is what catches a gradient. The composite
    /// reads one corner and writes it to all four, so a passthrough decision keyed off that corner would
    /// hand a gradient container to a path that cannot express one (see SF-33 for the composite's own
    /// half of this defect, which the identity check keeps unreachable).
    /// </summary>
    [Test]
    public void AGradientForcesTheBuffer()
    {
        var buffered = neutral();
        buffered.ColorInfo = ColorInfo.GradientHorizontal(Color.White, Color.Red);
        addOverlappingBoxes(buffered);

        bool tookTheBuffer = buffers(buffered);

        Assert.Multiple(() =>
        {
            Assert.That(buffered.Vertices[0].Color.Y, Is.EqualTo(1f).Within(1e-5f), "the corner the composite reads is neutral");
            Assert.That(buffered.Vertices[1].Color.Y, Is.EqualTo(0f).Within(1e-5f), "only the far corner is tinted");
            Assert.That(tookTheBuffer, Is.True, "which is exactly why all four corners have to be read");
        });
    }

    #endregion

    #region The independent clauses

    /// <summary>
    /// An effect needs a source texture to sample, so it needs the buffer.
    /// </summary>
    [Test]
    public void ABlurForcesTheBuffer()
    {
        var buffered = neutral();
        buffered.BlurSigma = new Vector2(4, 4);
        addOverlappingBoxes(buffered);

        Assert.That(buffers(buffered), Is.True);
    }

    [Test]
    public void GrayscaleForcesTheBuffer()
    {
        var buffered = neutral();
        buffered.GrayscaleStrength = 1f;
        addOverlappingBoxes(buffered);

        Assert.That(buffers(buffered), Is.True);
    }

    /// <summary>
    /// The clause that pins the gate on effect <em>intent</em> rather than on the draw path's
    /// <c>effectsActive</c>, which also requires a backend with raw-pass support.
    /// <see cref="HeadlessRenderer"/> has none, so a predicate keyed off the latter would pass through
    /// here while every shipping backend buffered — and every pixel test in this file would then be
    /// exercising a branch that never runs in production.
    /// </summary>
    [Test]
    public void ARequestedButUnsupportedEffectStillForcesTheBuffer()
    {
        var buffered = neutral();
        buffered.BlurSigma = new Vector2(4, 4);
        addOverlappingBoxes(buffered);

        root.Add(buffered);
        render();

        Assert.Multiple(() =>
        {
            Assert.That(buffered.SharedData.FrameBuffer, Is.Not.Null, "the buffer is taken on intent, not on backend capability");
            Assert.That(buffered.SharedData.FinalEffectBuffer, Is.Null, "and headless genuinely cannot run the pass");
        });
    }

    /// <summary>
    /// Caching exists to <em>keep</em> the drawn buffer across frames. A passthrough redraws the subtree
    /// every frame, which is the opposite of what was asked for — so this asserts the subtree is drawn
    /// once over two frames, not merely that a framebuffer exists.
    /// </summary>
    [Test]
    public void CachingForcesTheBufferAndStopsTheSubtreeBeingRedrawn()
    {
        var counting = new CountingBox { Size = new Vector2(width, height) };
        var buffered = neutral();
        buffered.CacheDrawnFrameBuffer = true;
        buffered.Add(counting);

        root.Add(buffered);
        render();
        render();

        Assert.Multiple(() =>
        {
            Assert.That(buffered.SharedData.FrameBuffer, Is.Not.Null);
            Assert.That(counting.DrawCount, Is.EqualTo(1), "the second frame must composite the cached buffer, not redraw the child");
        });
    }

    /// <summary>
    /// The contrast that gives the test above its meaning: without caching, a passthrough draws the
    /// subtree every frame, which is correct and is the cost the passthrough accepts.
    /// </summary>
    [Test]
    public void PassthroughDrawsTheSubtreeEveryFrame()
    {
        var counting = new CountingBox { Size = new Vector2(width, height) };
        var buffered = neutral();
        buffered.Add(counting);

        root.Add(buffered);
        render();
        render();

        Assert.Multiple(() =>
        {
            Assert.That(buffered.SharedData.FrameBuffer, Is.Null);
            Assert.That(counting.DrawCount, Is.EqualTo(2));
        });
    }

    /// <summary>
    /// The background colour is the buffer clear, and there is nothing to clear without a buffer.
    /// </summary>
    [Test]
    public void ABackgroundColorForcesTheBuffer()
    {
        var buffered = neutral();
        buffered.BackgroundColor = Color.Blue;
        addOverlappingBoxes(buffered);

        Assert.That(buffers(buffered), Is.True);
    }

    /// <summary>
    /// A resolution change is a deliberate request to render at a different size and scale back up.
    /// </summary>
    [Test]
    public void AFrameBufferScaleForcesTheBuffer()
    {
        var buffered = neutral();
        buffered.FrameBufferScale = new Vector2(0.5f, 0.5f);
        addOverlappingBoxes(buffered);

        Assert.That(buffers(buffered), Is.True);
    }

    /// <summary>
    /// A non-default blend applies to the flattened result. Passed through it would apply to each child
    /// separately instead, which for an additive blend over an overlap is visibly different.
    /// </summary>
    [Test]
    public void ANonDefaultBlendingForcesTheBuffer()
    {
        var buffered = neutral();
        buffered.Blending = BlendingMode.Additive;
        addOverlappingBoxes(buffered);

        Assert.That(buffers(buffered), Is.True);
    }

    #endregion

    #region Buffer lifetime

    /// <summary>
    /// A container that leaves the passthrough state gets its buffer back — the release is not a latch.
    /// </summary>
    [Test]
    public void LeavingThePassthroughStateTakesTheBufferAgain()
    {
        var buffered = neutral();
        addOverlappingBoxes(buffered);
        root.Add(buffered);

        render();
        Assert.That(buffered.SharedData.FrameBuffer, Is.Null, "passing through");

        buffered.Alpha = 0.5f;
        render();

        Assert.That(buffered.SharedData.FrameBuffer, Is.Not.Null, "a fade brings the buffer back");
    }

    /// <summary>
    /// An already-allocated buffer is not freed on the first passthrough frame. A container that toggles
    /// an effect — the motivating case — would otherwise allocate and free a full-screen render target on
    /// every toggle, which is the churn this is meant to remove rather than relocate.
    /// </summary>
    [Test]
    public void AnAllocatedBufferSurvivesAShortPassthrough()
    {
        var buffered = neutral();
        buffered.BlurSigma = new Vector2(4, 4);
        addOverlappingBoxes(buffered);
        root.Add(buffered);

        render();
        Assert.That(buffered.SharedData.FrameBuffer, Is.Not.Null, "the blur allocated it");

        buffered.BlurSigma = Vector2.Zero;

        for (int i = 0; i < 5; i++)
            render();

        Assert.That(buffered.SharedData.FrameBuffer, Is.Not.Null, "kept across a brief toggle");
    }

    /// <summary>
    /// But a passthrough that settles does eventually give the memory back, which is the one-off VRAM win.
    /// </summary>
    [Test]
    public void AnAllocatedBufferIsReleasedOnceThePassthroughSettles()
    {
        var buffered = neutral();
        buffered.BlurSigma = new Vector2(4, 4);
        addOverlappingBoxes(buffered);
        root.Add(buffered);

        render();
        Assert.That(buffered.SharedData.FrameBuffer, Is.Not.Null);

        buffered.BlurSigma = Vector2.Zero;

        for (int i = 0; i < 120; i++)
            render();

        Assert.That(buffered.SharedData.FrameBuffer, Is.Null, "settled passthrough releases the render target");
    }

    #endregion

    /// <summary>
    /// A box that counts the frames it was actually drawn on, which is how the caching clause is asserted:
    /// the observable difference between buffering and passing through is whether the subtree is
    /// re-submitted, not what the result looks like.
    /// </summary>
    private partial class CountingBox : Drawable
    {
        public int DrawCount => counter.Count;

        private readonly DrawCounter counter = new DrawCounter();

        protected override DrawNode CreateDrawNode() => new CountingDrawNode(counter);

        /// <summary>
        /// Shared between the drawable and all three of its draw nodes, since which node draws a given
        /// frame is not something a test can control.
        /// </summary>
        private class DrawCounter
        {
            public int Count;
        }

        private class CountingDrawNode : DrawNode
        {
            private readonly DrawCounter counter;

            public CountingDrawNode(DrawCounter counter)
            {
                this.counter = counter;
            }

            public override void Draw(IRenderer renderer)
            {
                counter.Count++;
                base.Draw(renderer);
            }
        }
    }
}
