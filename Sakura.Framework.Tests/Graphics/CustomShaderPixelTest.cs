// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// A game's own shader in pixels, on Vulkan (see <see cref="PixelCapture"/>): the only backend that can draw with no window, so the
/// only one that runs here. The reference rasterizer has no shaders, so it has no fixture.
/// </summary>
[TestFixture(PixelCapture.VULKAN)]
public class CustomShaderPixelTest
{
    private const int surface = 64;

    private static readonly CustomShaderStore store = new CustomShaderStore(new EmbeddedResourceStorage(typeof(TestApp).Assembly, "Sakura.Framework.Tests.Resources.Shaders"));

    private static readonly Vector4 teal = new Vector4(0.1f, 0.6f, 0.7f, 1);
    private static readonly Vector4 amber = new Vector4(0.8f, 0.5f, 0.1f, 1);

    private readonly string rendererKind;
    private DrawablePixels pixels = null!;

    public CustomShaderPixelTest(string rendererKind)
    {
        this.rendererKind = rendererKind;
    }

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        Logger.Initialize();
        pixels = new DrawablePixels(rendererKind, surface, surface);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        pixels?.Dispose();
        Logger.Shutdown();
    }

    [SetUp]
    public void SetUp() => pixels.Reset();

    private void assertColor(Vector4 actual, Vector4 expected, string message)
    {
        Assert.That(actual.X, Is.EqualTo(expected.X).Within(pixels.Tolerance), $"{message} (red)");
        Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(pixels.Tolerance), $"{message} (green)");
        Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(pixels.Tolerance), $"{message} (blue)");
    }

    [Test]
    public void ItsBlockReachesTheShader()
    {
        pixels.Root.Add(new Solid(teal) { Size = new Vector2(surface) });

        assertColor(pixels.Render()[32, 32], teal, "the block's color");
    }

    /// <summary>
    /// The block is uploaded for each draw, not once per shader: two of the same shader in a frame each get their own.
    /// </summary>
    [Test]
    public void TwoDrawablesWithOneShaderKeepTheirOwnValues()
    {
        pixels.Root.Add(new Solid(teal) { Size = new Vector2(32, surface) });
        pixels.Root.Add(new Solid(amber) { Size = new Vector2(32, surface), X = 32 });

        PixelSurface screen = pixels.Render();

        Assert.Multiple(() =>
        {
            assertColor(screen[10, 32], teal, "the first");
            assertColor(screen[50, 32], amber, "the second");
        });
    }

    [Test]
    public void ChangingItsValueReachesTheScreen()
    {
        var solid = new Solid(teal) { Size = new Vector2(surface) };
        pixels.Root.Add(solid);
        pixels.Render();

        solid.Value = amber;

        assertColor(pixels.Render()[32, 32], amber, "the new color");
    }

    /// <summary>
    /// Blocks found by name when their order and number don't match what's declared: this one has three, out of order, and one of
    /// them never read.
    /// </summary>
    [Test]
    public void SeveralBlocksEachGoWhereTheyreNamed()
    {
        pixels.Root.Add(new TwoBlocks { Size = new Vector2(surface), Left = teal, Right = amber });

        PixelSurface screen = pixels.Render();

        Assert.Multiple(() =>
        {
            assertColor(screen[10, 32], teal, "the left");
            assertColor(screen[50, 32], amber, "the right");
        });
    }

    /// <summary>
    /// With a vertex shader of its own, a block both stages read.
    /// </summary>
    [Test]
    public void ABlockReadInBothStagesReachesBoth()
    {
        pixels.Root.Add(new BothStages { Size = new Vector2(surface), Value = new Vector4(0.5f, 0.25f, 0.75f, 1) });

        assertColor(pixels.Render()[32, 32], new Vector4(0.25f, 0.0625f, 0.5625f, 1), "the color through both, so squared");
    }

    /// <summary>
    /// A draw node has to leave the renderer as it found it: anything drawn after it uses the framework's shader, with the
    /// framework's projection and no leftover clip.
    /// </summary>
    [Test]
    public void TheFrameworksDrawingAfterItIsUntouched()
    {
        pixels.Root.Add(new Solid(teal) { Size = new Vector2(32, surface) });
        pixels.Root.Add(new Box { Size = new Vector2(32, surface), X = 32, Color = Color.FromArgb(255, 0, 0) });

        PixelSurface screen = pixels.Render();

        Assert.Multiple(() =>
        {
            assertColor(screen[10, 32], teal, "the shader's");
            assertColor(screen[50, 32], new Vector4(1, 0, 0, 1), "the box after it");
            assertColor(screen[33, 1], new Vector4(1, 0, 0, 1), "right to the box's corner");
        });
    }

    /// <summary>
    /// And what's drawn before it isn't drawn over it: anything batched has to be flushed first.
    /// </summary>
    [Test]
    public void TheFrameworksDrawingBeforeItStaysBehindIt()
    {
        pixels.Root.Add(new Box { Size = new Vector2(surface), Color = Color.FromArgb(255, 0, 0) });
        pixels.Root.Add(new Solid(teal) { Size = new Vector2(32, surface) });

        PixelSurface screen = pixels.Render();

        Assert.Multiple(() =>
        {
            assertColor(screen[10, 32], teal, "over the box");
            assertColor(screen[50, 32], new Vector4(1, 0, 0, 1), "the box, beside it");
        });
    }

    [Test]
    public void AMaskClipsIt()
    {
        pixels.Root.Add(new Container
        {
            Position = new Vector2(16),
            Size = new Vector2(32),
            Masking = true,
            Child = new Solid(teal) { Position = new Vector2(-16), Size = new Vector2(surface) }
        });

        PixelSurface screen = pixels.Render();

        Assert.Multiple(() =>
        {
            assertColor(screen[32, 32], teal, "inside the mask");
            assertColor(screen[8, 32], new Vector4(0, 0, 0, 1), "left of it");
            assertColor(screen[56, 32], new Vector4(0, 0, 0, 1), "right of it");
            assertColor(screen[32, 8], new Vector4(0, 0, 0, 1), "above it");
            assertColor(screen[32, 56], new Vector4(0, 0, 0, 1), "below it");
        });
    }

    [Test]
    public void AMasksCornerClipsIt()
    {
        pixels.Root.Add(new Container
        {
            Size = new Vector2(surface),
            Masking = true,
            CornerRadius = 20,
            Child = new Solid(teal) { Size = new Vector2(surface) }
        });

        PixelSurface screen = pixels.Render();

        Assert.Multiple(() =>
        {
            assertColor(screen[1, 1], new Vector4(0, 0, 0, 1), "out past the corner");
            assertColor(screen[32, 1], teal, "along the edge, between the corners");
        });
    }

    [Test]
    public void AFadingMaskFadesIt()
    {
        pixels.Root.Add(new Container
        {
            Size = new Vector2(surface),
            Masking = true,
            MaskingFade = new MarginPadding { Top = 32 },
            Child = new Solid(new Vector4(1)) { Size = new Vector2(surface) }
        });

        PixelSurface screen = pixels.Render();

        Assert.Multiple(() =>
        {
            Assert.That(screen[32, 1].X, Is.LessThan(0.05f), "at the edge it fades from");
            Assert.That(screen[32, 16].X, Is.EqualTo(0.5f).Within(0.05f), "half way through the fade");
            assertColor(screen[32, 50], new Vector4(1), "past it");
        });
    }

    [Test]
    public void ItBlendsAsTheDrawableSays()
    {
        pixels.Root.Add(new Box { Size = new Vector2(surface), Color = Color.FromArgb(64, 0, 0) });
        pixels.Root.Add(new Solid(new Vector4(0.25f, 0.25f, 0, 1)) { Size = new Vector2(32, surface), Blending = BlendingMode.Additive });

        Vector4 added = pixels.Render()[10, 32];
        Vector4 background = pixels.Render()[50, 32];

        Assert.Multiple(() =>
        {
            Assert.That(added.X, Is.EqualTo(background.X + 0.25f).Within(pixels.Tolerance), "added to the red that's there");
            Assert.That(added.Y, Is.EqualTo(0.25f).Within(pixels.Tolerance), "its green over none");
        });
    }

    /// <summary>
    /// The framework's own fade doesn't reach a shader's pixels by itself: a drawable says how faded it is through its block. And at
    /// no alpha it's left out.
    /// </summary>
    [Test]
    public void ADrawableFadesItThroughItsBlock()
    {
        var solid = new Solid(new Vector4(1), fades: true) { Size = new Vector2(surface), Alpha = 0.5f };
        pixels.Root.Add(solid);

        Assert.That(pixels.Render()[32, 32].X, Is.EqualTo(0.5f).Within(pixels.Tolerance), "half faded");

        solid.Alpha = 0;

        Assert.That(pixels.Render()[32, 32].X, Is.Zero.Within(pixels.Tolerance), "faded right out");
    }

    /// <summary>
    /// In a framebuffer the backend has a different target bound, which is where a shader's blocks and pipeline could be lost.
    /// </summary>
    [Test]
    public void ItDrawsInsideABufferedContainer()
    {
        using var host = new HeadlessAppHost(nameof(CustomShaderPixelTest));

        var cachingHost = new HostCaching { Size = new Vector2(surface) };
        pixels.Root.Add(cachingHost);
        cachingHost.CacheHost(host);

        cachingHost.Add(new BufferedContainer
        {
            Size = new Vector2(surface),
            Child = new Solid(teal) { Size = new Vector2(surface) }
        });

        assertColor(pixels.Render()[32, 32], teal, "through the framebuffer");
    }

    [Test]
    public void AShaderThatWontBuildIsLeftOut()
    {
        pixels.Root.Add(new Box { Size = new Vector2(surface), Color = Color.FromArgb(255, 0, 0) });
        pixels.Root.Add(new Broken { Size = new Vector2(surface) });

        assertColor(pixels.Render()[32, 32], new Vector4(1, 0, 0, 1), "what's behind it, and the frame still drawn");
    }

    private struct SolidBlock
    {
        public Vector4 Color;
    }

    private partial class HostCaching : Container
    {
        public void CacheHost(AppHost value) => Cache(value);
    }

    private partial class Solid : Drawable
    {
        private readonly bool fades;

        public Vector4 Value;

        public Solid(Vector4 value, bool fades = false)
        {
            Value = value;
            this.fades = fades;
        }

        private SolidBlock block() => new SolidBlock
        {
            Color = fades ? new Vector4(Value.X * DrawAlpha, Value.Y * DrawAlpha, Value.Z * DrawAlpha, 1) : Value
        };

        public override void Update()
        {
            Invalidate(InvalidationFlags.DrawInfo);
            base.Update();
        }

        protected override DrawNode CreateDrawNode() => new CustomShaderDrawNode<SolidBlock>(store, "test-solid.frag", "SolidBlock", block);
    }

    private partial class TwoBlocks : Drawable
    {
        public Vector4 Left;
        public Vector4 Right;

        protected override DrawNode CreateDrawNode() => new Node(this);

        private class Node : CustomShaderDrawNode
        {
            private readonly TwoBlocks source;
            private SolidBlock left;
            private SolidBlock right;

            public Node(TwoBlocks source)
                : base(store, "test-two-blocks.frag")
            {
                this.source = source;
            }

            public override void ApplyState(Drawable drawable)
            {
                base.ApplyState(drawable);
                left = new SolidBlock { Color = source.Left };
                right = new SolidBlock { Color = source.Right };
            }

            protected override void SetUniforms(IShader shader)
            {
                shader.SetUniformBlock("LeftBlock", left);
                shader.SetUniformBlock("RightBlock", right);
            }
        }
    }

    private partial class BothStages : Drawable
    {
        public Vector4 Value;

        protected override DrawNode CreateDrawNode() =>
            new CustomShaderDrawNode<SolidBlock>(store, "test-both-stages.frag", "SharedBlock", () => new SolidBlock { Color = Value }, "test-both-stages.vert");
    }

    private partial class Broken : Drawable
    {
        protected override DrawNode CreateDrawNode() => new CustomShaderDrawNode(store, "test-broken.frag");
    }
}
