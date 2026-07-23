// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.IO;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Timing;
using VertexData = Sakura.Framework.Graphics.Rendering.Vertex.Vertex;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// Regression test of https://github.com/HelloYeew/sakura/pull/152
/// </summary>
[TestFixture]
public class ContainerMaskScaleTest
{
    private ManualClock manual = null!;
    private Container root = null!;

    [OneTimeSetUp]
    public void InitializeLogger() => Logger.Initialize();

    [OneTimeTearDown]
    public void ShutdownLogger() => Logger.Shutdown();

    [SetUp]
    public void SetUp()
    {
        manual = new ManualClock { CurrentTime = 1000 };
        root = new Container
        {
            Size = new Vector2(800, 600),
            Clock = new FramedClock(manual)
        };

        root.Load();
        root.LoadComplete();
    }

    private void frame(double advanceMs = 16)
    {
        manual.CurrentTime += advanceMs;
        root.UpdateSubTree();
    }

    private void settle()
    {
        for (int i = 0; i < 5; i++)
            frame();
    }

    /// <summary>
    /// Renders the container's draw node and returns the single mask that was pushed.
    /// </summary>
    private static RecordingRenderer.MaskCall pushDrawNode(Container container)
    {
        var recorder = new RecordingRenderer();
        var node = (ContainerDrawNode)container.GenerateDrawNode(0);
        node.Draw(recorder);

        Assert.That(recorder.Pushes, Has.Count.EqualTo(1), "Masking container must push exactly one mask.");
        return recorder.Pushes[0];
    }

    /// <summary>
    /// A <see cref="CircularContainer"/> under a 2x ancestor scale. Before the fix the mask received the
    /// unscaled radius (DrawSize/2 in logical space) against a doubled screen half-size, producing a
    /// squircle. After the fix the radius scales too, so radius == half-size on both axes — a true circle.
    /// </summary>
    [Test]
    public void TestCircularContainerStaysCircleUnderScale()
    {
        CircularContainer circle = null!;

        var scaled = new Container
        {
            Scale = new Vector2(2f),
            Size = new Vector2(200, 200),
            Child = circle = new CircularContainer
            {
                Size = new Vector2(100, 100)
            }
        };

        root.Add(scaled);
        settle();

        var mask = pushDrawNode(circle);

        Assert.Multiple(() =>
        {
            // 100px logical -> 50px logical radius -> 100px screen half-size at 2x scale.
            Assert.That(mask.HalfSize.X, Is.EqualTo(100).Within(0.05f), "Screen half-size must reflect the 2x scale.");
            Assert.That(mask.HalfSize.Y, Is.EqualTo(100).Within(0.05f));

            // The circle invariant: corner radius must equal the screen half-size, not the logical 50.
            Assert.That(mask.CornerRadius, Is.EqualTo(mask.HalfSize.X).Within(0.05f),
                "CircularContainer corner radius must scale with the half-size to stay a circle under scale.");
        });
    }

    /// <summary>
    /// A fractional (0.5x) scale must shrink the radius in lock-step too, so the shape over-rounds neither
    /// way. Radius stays equal to the (halved) screen half-size.
    /// </summary>
    [Test]
    public void TestCircularContainerStaysCircleUnderDownscale()
    {
        CircularContainer circle = null!;

        var scaled = new Container
        {
            Scale = new Vector2(0.5f),
            Size = new Vector2(200, 200),
            Child = circle = new CircularContainer
            {
                Size = new Vector2(100, 100)
            }
        };

        root.Add(scaled);
        settle();

        var mask = pushDrawNode(circle);

        Assert.Multiple(() =>
        {
            Assert.That(mask.HalfSize.X, Is.EqualTo(25).Within(0.05f), "100px at 0.5x → 25px screen half-size.");
            Assert.That(mask.CornerRadius, Is.EqualTo(mask.HalfSize.X).Within(0.05f),
                "Corner radius must track the down-scaled half-size.");
        });
    }

    /// <summary>
    /// At scale 1 the radius is unchanged — the fix must not regress the common unscaled case.
    /// </summary>
    [Test]
    public void TestUnscaledCornerRadiusUnchanged()
    {
        Container box;

        var parent = new Container
        {
            Size = new Vector2(200, 200),
            Child = box = new Container
            {
                Size = new Vector2(120, 80),
                Masking = true,
                CornerRadius = 16
            }
        };

        root.Add(parent);
        settle();

        var mask = pushDrawNode(box);

        Assert.That(mask.CornerRadius, Is.EqualTo(16).Within(0.05f),
            "At scale 1 the corner radius must equal the authored logical value.");
    }

    /// <summary>
    /// A rounded, bordered container under a 2x scale: both the corner radius and the border thickness
    /// are logical-space values that must be scaled into screen space when popped, so the border keeps a
    /// consistent visual weight and corner curvature under scale.
    /// </summary>
    [Test]
    public void TestBorderThicknessAndRadiusScaleTogether()
    {
        Container bordered = null!;

        var scaled = new Container
        {
            Scale = new Vector2(2f),
            Size = new Vector2(300, 300),
            Child = bordered = new Container
            {
                Size = new Vector2(100, 100),
                Masking = true,
                CornerRadius = 10,
                BorderThickness = 4,
                BorderColor = Color.White
            }
        };

        root.Add(scaled);
        settle();

        var recorder = new RecordingRenderer();
        var node = (ContainerDrawNode)bordered.GenerateDrawNode(0);
        node.Draw(recorder);

        Assert.That(recorder.Pops, Has.Count.EqualTo(1), "Masking container must pop exactly one mask.");
        var pop = recorder.Pops[0];

        Assert.Multiple(() =>
        {
            Assert.That(pop.CornerRadius, Is.EqualTo(20).Within(0.05f), "CornerRadius 10 at 2x → 20 screen.");
            Assert.That(pop.BorderThickness, Is.EqualTo(8).Within(0.05f), "BorderThickness 4 at 2x → 8 screen.");
        });
    }

    /// <summary>
    /// Minimal <see cref="IRenderer"/> that records the geometry passed to <c>PushMask</c>/<c>PopMask</c>.
    /// Everything else is a no-op — <see cref="ContainerDrawNode.Draw"/> only touches these masking calls
    /// (its child list is empty when the node is generated in isolation).
    /// </summary>
    private sealed class RecordingRenderer : IRenderer
    {
        public readonly struct MaskCall
        {
            public MaskCall(Vector2 center, Vector2 halfSize, float shearX, float cornerRadius, float borderThickness)
            {
                Center = center;
                HalfSize = halfSize;
                ShearX = shearX;
                CornerRadius = cornerRadius;
                BorderThickness = borderThickness;
            }

            public Vector2 Center { get; }
            public Vector2 HalfSize { get; }
            public float ShearX { get; }
            public float CornerRadius { get; }
            public float BorderThickness { get; }
        }

        public List<MaskCall> Pushes { get; } = new();
        public List<MaskCall> Pops { get; } = new();

        public void PushMask(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius)
            => Pushes.Add(new MaskCall(maskCenter, maskHalfSize, shearX, cornerRadius, 0f));

        public void PopMask(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius, float borderThickness, Color borderColor, ReadOnlySpan<VertexData> maskVertices = default)
            => Pops.Add(new MaskCall(maskCenter, maskHalfSize, shearX, cornerRadius, borderThickness));

        public void DrawEdgeEffect(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius, float edgeRadius, Vector2 offset, Color color, bool glow, bool hollow, ReadOnlySpan<VertexData> quadVertices) { }

        // unused members
        public Texture WhitePixel => throw new NotSupportedException();
        public Matrix4x4 ProjectionMatrix => default;
        public Storage ShaderStorage { get; set; } = null!;
        public DiskCache ShaderCache { get; set; } = null!;
        public Vector2 RenderScale => Vector2.One;

        void IRenderer.Initialize(IGraphicsSurface graphicsSurface) { }
        public void Clear() { }
        public void StartFrame() { }
        public void SetRoot(DrawNode rootDrawNode) { }
        public void Resize(int physicalWidth, int physicalHeight, int logicalWidth, int logicalHeight) { }
        public void Draw(IClock clock) { }
        public void DrawVertices(ReadOnlySpan<VertexData> vertices, Texture textureGl) { }
        public void DrawQuads(ReadOnlySpan<VertexData> vertices, Texture textureGl) { }
        public void SetBlendMode(BlendingMode blendingMode) { }
        public void ScheduleToDrawThread(Action action) { }
        public void FlushBatch() { }
        public void RestoreMainShader() { }
        public IShader CreateShader(Storage storage, string vertexPath, string fragmentPath) => throw new NotSupportedException();
        public INativeVideoTexture CreateVideoTexture(int width, int height) => throw new NotSupportedException();
        public INativeTexture CreateNativeTexture(int width, int height) => throw new NotSupportedException();
        public IFrameBuffer CreateFrameBuffer(int width, int height, bool pixelSnapping = false) => throw new NotSupportedException();
        public void BindFrameBuffer(IFrameBuffer frameBuffer, RectangleF sourceRect, Color clearColor = default) { }
        public void UnbindFrameBuffer() { }
    }
}
