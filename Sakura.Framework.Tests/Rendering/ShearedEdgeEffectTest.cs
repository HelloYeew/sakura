// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
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
/// A sheared container's shadow or glow is drawn over a quad that leans the way the container does and
/// is wide enough for the falloff. The quad used to lean the other way, so the shader's (correctly
/// leaning) shape was cut off at two corners: a halo round a slanted button was clipped at its top
/// right and bottom left.
/// </summary>
[TestFixture]
public class ShearedEdgeEffectTest
{
    private ManualClock manual = null!;
    private Container root = null!;
    private Container target = null!;

    [OneTimeSetUp]
    public void InitializeLogger() => Logger.Initialize();

    [OneTimeTearDown]
    public void ShutdownLogger() => Logger.Shutdown();

    private void build(float shear, EdgeEffectType type)
    {
        manual = new ManualClock { CurrentTime = 1000 };
        root = new Container
        {
            Size = new Vector2(800, 600),
            Clock = new FramedClock(manual)
        };

        root.Add(target = new Container
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Size = new Vector2(300, 60),
            Shear = new Vector2(shear, 0),
            Masking = true,
            CornerRadius = 6,
            EdgeEffect = new EdgeEffectParameters
            {
                Type = type,
                Color = Color.Red,
                Radius = 16
            }
        });

        root.Load();
        root.CompleteLoad();

        for (int i = 0; i < 3; i++)
        {
            manual.CurrentTime += 16;
            root.UpdateSubTree();
        }
    }

    private Vector2[] drawnQuad()
    {
        var recorder = new RecordingRenderer();
        root.GenerateDrawNodeSubtree(0).Draw(recorder);
        Assert.That(recorder.Quad, Is.Not.Null, "The effect was drawn.");
        return recorder.Quad!;
    }

    /// <summary>The container's corners on screen, pushed out by <paramref name="by"/> along each axis.</summary>
    private Vector2[] corners(float by)
    {
        Vector2 local(float x, float y) => target.ToScreenSpace(new Vector2(x, y));
        var size = target.DrawSize;

        return new[]
        {
            local(0, 0) + new Vector2(-by, -by),
            local(size.X, 0) + new Vector2(by, -by),
            local(size.X, size.Y) + new Vector2(by, by),
            local(0, size.Y) + new Vector2(-by, by)
        };
    }

    [TestCase(-0.66f, EdgeEffectType.Shadow)]
    [TestCase(0.66f, EdgeEffectType.Shadow)]
    [TestCase(-0.66f, EdgeEffectType.Glow)]
    [TestCase(0, EdgeEffectType.Shadow)]
    public void TestQuadCoversTheLeaningShapeAndItsFalloff(float shear, EdgeEffectType type)
    {
        build(shear, type);

        var quad = drawnQuad();

        // Every corner, and a little of the falloff past it, inside the quad drawn.
        foreach (var corner in corners(8))
            Assert.That(inside(quad, corner), Is.True, $"{corner} lies outside the drawn quad (shear {shear}).");
    }

    private static bool inside(Vector2[] quad, Vector2 point)
    {
        // Convex, in order: the point is on the same side of every edge.
        float sign = 0;

        for (int i = 0; i < 4; i++)
        {
            Vector2 a = quad[i], b = quad[(i + 1) % 4];
            float cross = (b.X - a.X) * (point.Y - a.Y) - (b.Y - a.Y) * (point.X - a.X);

            if (Math.Abs(cross) < 1e-3f)
                continue;

            if (sign == 0)
                sign = Math.Sign(cross);
            else if (Math.Sign(cross) != sign)
                return false;
        }

        return true;
    }

    private sealed class RecordingRenderer : IRenderer
    {
        private static readonly HeadlessTextureManager textures = new HeadlessTextureManager();

        public Vector2[]? Quad { get; private set; }

        public void DrawEdgeEffect(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius, float edgeRadius, Vector2 offset, Color color, bool glow, bool hollow, ReadOnlySpan<VertexData> quadVertices)
        {
            Quad = new[] { quadVertices[0].Position, quadVertices[1].Position, quadVertices[2].Position, quadVertices[3].Position };
        }

        public void SetBlendMode(BlendingMode blendingMode) { }
        public void PopMask(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius, float borderThickness, Color borderColor, ReadOnlySpan<VertexData> maskVertices = default) { }
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
