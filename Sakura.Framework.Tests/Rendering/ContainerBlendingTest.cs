// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.IO;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Timing;
using VertexData = Sakura.Framework.Graphics.Rendering.Vertex.Vertex;

namespace Sakura.Framework.Tests.Rendering;

/// <summary>
/// A container's own drawing (its border, its shadow) is in the container's blending, not in whatever
/// the drawable drawn before it left the renderer in. A sibling flashing additively just before a ring
/// (a clicked button's flash behind its icon) used to draw the ring's border additively, lighting it up
/// white until the flash faded.
/// </summary>
[TestFixture]
public class ContainerBlendingTest
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
            Size = new Vector2(400, 300),
            Clock = new FramedClock(manual)
        };

        root.Load();
        root.CompleteLoad();
    }

    private RecordingRenderer draw()
    {
        for (int i = 0; i < 3; i++)
        {
            manual.CurrentTime += 16;
            root.UpdateSubTree();
        }

        var recorder = new RecordingRenderer();
        recorder.SetBlendMode(BlendingMode.Alpha);
        root.GenerateDrawNodeSubtree(0).Draw(recorder);
        return recorder;
    }

    /// <summary>An additive flash, then the container under test, as a clicked button lays them out.</summary>
    private void addFlashThen(Drawable drawable)
    {
        root.Add(new Box
        {
            Size = new Vector2(200),
            Alpha = 0.4f,
            Blending = BlendingMode.Additive
        });
        root.Add(drawable);
    }

    [Test]
    public void TestBorderAfterAnAdditiveSiblingIsDrawnInItsOwnBlending()
    {
        // The clock's face: a ring whose only child is invisible, so nothing inside it sets the blending.
        addFlashThen(new Container
        {
            Size = new Vector2(40),
            Masking = true,
            CornerRadius = 20,
            BorderThickness = 2,
            BorderColor = Color.Red,
            Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true }
        });

        var recorder = draw();

        Assert.That(recorder.Borders, Has.Count.EqualTo(1));
        Assert.That(recorder.Borders[0], Is.EqualTo(BlendingMode.Alpha), "The ring's border must not be drawn additively.");
    }

    [Test]
    public void TestBorderAfterAnAdditiveChildIsDrawnInItsOwnBlending()
    {
        // The last thing drawn inside it is additive: the border, drawn after, is still the container's.
        var ring = new Container
        {
            Size = new Vector2(40),
            Masking = true,
            BorderThickness = 2,
            BorderColor = Color.Red,
            Child = new Box { RelativeSizeAxes = Axes.Both, Blending = BlendingMode.Additive }
        };
        root.Add(ring);

        var recorder = draw();

        Assert.That(recorder.Borders, Is.EqualTo(new[] { BlendingMode.Alpha }));
    }

    [Test]
    public void TestShadowAfterAnAdditiveSiblingIsDrawnInItsOwnBlending()
    {
        // A black shadow drawn additively adds nothing: it would vanish.
        addFlashThen(new Container
        {
            Size = new Vector2(40),
            EdgeEffect = new EdgeEffectParameters
            {
                Type = EdgeEffectType.Shadow,
                Color = Color.Black,
                Radius = 10
            }
        });

        var recorder = draw();

        Assert.That(recorder.Shadows, Is.EqualTo(new[] { BlendingMode.Alpha }));
    }

    [Test]
    public void TestAnAdditiveContainerKeepsItsOwnBlending()
    {
        // The container's own setting still decides: one set to additive draws its border additively.
        root.Add(new Container
        {
            Size = new Vector2(40),
            Masking = true,
            BorderThickness = 2,
            BorderColor = Color.Red,
            Blending = BlendingMode.Additive
        });

        var recorder = draw();

        Assert.That(recorder.Borders, Is.EqualTo(new[] { BlendingMode.Additive }));
    }

    /// <summary>
    /// Records the blending in force when each border and shadow is drawn. Everything else is a no-op.
    /// </summary>
    private sealed class RecordingRenderer : IRenderer
    {
        private static readonly HeadlessTextureManager textures = new HeadlessTextureManager();

        private BlendingMode current;

        public List<BlendingMode> Borders { get; } = new List<BlendingMode>();
        public List<BlendingMode> Shadows { get; } = new List<BlendingMode>();

        public void SetBlendMode(BlendingMode blendingMode) => current = blendingMode;

        public void PopMask(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius, float borderThickness, Color borderColor, ReadOnlySpan<VertexData> maskVertices = default)
        {
            if (borderThickness > 0)
                Borders.Add(current);
        }

        public void DrawEdgeEffect(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius, float edgeRadius, Vector2 offset, Color color, bool glow, bool hollow, ReadOnlySpan<VertexData> quadVertices)
        {
            if (!glow)
                Shadows.Add(current);
        }

        public void PushMask(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius) { }
        public void ApplyCurrentClip(Span<VertexData> vertices) { }

        // unused members
        public Texture WhitePixel => textures.WhitePixel;
        public Matrix4x4 ProjectionMatrix => default;
        public Storage ShaderStorage { get; set; } = null!;
        public DiskCache ShaderCache { get; set; } = null!;
        public Vector2 RenderScale => Vector2.One;

        void IRenderer.Initialize(IGraphicsSurface graphicsSurface) { }
        public void Clear() { }
        public void StartFrame() { }
        public void SetRoot(DrawNode rootDrawNode) { }
        public void Resize(int physicalWidth, int physicalHeight, int logicalWidth, int logicalHeight) { }
        public double LastBlockedMilliseconds => 0;

        public void Draw(IClock clock) { }
        public void DrawVertices(ReadOnlySpan<VertexData> vertices, Texture textureGl) { }
        public void DrawQuads(ReadOnlySpan<VertexData> vertices, Texture textureGl) { }
        public void ScheduleToDrawThread(Action action) { }
        public void FlushBatch() { }
        public void RestoreMainShader() { }
        public IShader CreateShader(Storage storage, string vertexPath, string fragmentPath) => throw new NotSupportedException();
        public INativeVideoTexture CreateVideoTexture(int width, int height, VideoPlaneLayout layout, bool fromHardwareFrame = false) => throw new NotSupportedException();
        public unsafe bool CanSampleHardwareFrame(FFmpeg.AutoGen.AVFrame* frame) => false;
        public INativeTexture CreateNativeTexture(int width, int height) => throw new NotSupportedException();
        public IFrameBuffer CreateFrameBuffer(int width, int height, bool pixelSnapping = false) => throw new NotSupportedException();
        public void BindFrameBuffer(IFrameBuffer frameBuffer, RectangleF sourceRect, Color clearColor = default) { }
        public void UnbindFrameBuffer() { }
    }
}
