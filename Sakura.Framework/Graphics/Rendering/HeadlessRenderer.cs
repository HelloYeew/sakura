// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.IO;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Graphics.Rendering;

public class HeadlessRenderer : IRenderer
{
    public Texture WhitePixel { get; }
    public Matrix4x4 ProjectionMatrix => default;
    public Storage ShaderStorage { get; set; }
    public DiskCache ShaderCache { get; set; }
    private readonly HeadlessTextureManager textureManager;

    public HeadlessRenderer(HeadlessTextureManager textureManager)
    {
        this.textureManager = textureManager;
        WhitePixel = textureManager.WhitePixel;
    }

    #region Pixel capture

    private HeadlessRasterizer? rasterizer;
    private PixelSurface? screen;
    private BlendingMode currentBlendMode = BlendingMode.Alpha;

    // The target currently being rendered into, innermost last. Empty means the screen surface.
    private readonly List<(PixelSurface Surface, HeadlessRasterizer.ViewportTransform Viewport)> boundTargets = new List<(PixelSurface, HeadlessRasterizer.ViewportTransform)>();

    /// <summary>
    /// Whether draw calls are being rasterized into readable surfaces. Off by default.
    /// </summary>
    public bool PixelCaptureEnabled => rasterizer != null;

    /// <summary>
    /// The screen render target. Only valid once <see cref="EnablePixelCapture"/> has been called.
    /// </summary>
    public PixelSurface Screen =>
        screen ?? throw new InvalidOperationException($"Pixel capture is off. Call {nameof(EnablePixelCapture)} first.");

    /// <summary>
    /// Turns on software rasterization so drawing can be read back and asserted, with a screen surface of
    /// the given size.
    /// </summary>
    /// <remarks>
    /// <para>
    /// framebuffers in sakura are sized in physical pixels to their on-screen bounds,
    /// so a test that resizes one to a retina full-screen 2578x1914 would otherwise silently
    /// allocate ~79 MB of float RGBA it never reads. Leaving capture off
    /// keeps every existing test on exactly the no-op path it was written against.
    /// </para>
    /// <para>
    /// See <see cref="HeadlessRasterizer"/> for what the rasterizer does and does not model. It answers
    /// questions about blending and compositing; it is not evidence about a real driver.
    /// </para>
    /// </remarks>
    public void EnablePixelCapture(int width, int height)
    {
        rasterizer = new HeadlessRasterizer();
        screen = new PixelSurface(width, height);
        boundTargets.Clear();
        currentBlendMode = BlendingMode.Alpha;
    }

    /// <summary>
    /// The surface draw calls currently land on, or null when capture is off.
    /// </summary>
    private PixelSurface? currentTarget => boundTargets.Count > 0 ? boundTargets[^1].Surface : screen;

    private HeadlessRasterizer.ViewportTransform? currentViewport => boundTargets.Count > 0 ? boundTargets[^1].Viewport : null;

    /// <summary>
    /// The CPU pixels behind a texture, or null to sample it as opaque white.
    /// </summary>
    private static PixelSurface? surfaceOf(Texture texture)
        => (texture.BackendTexture as HeadlessNativeTexture)?.Surface;

    #endregion

    public void Initialize(IGraphicsSurface graphicsSurface)
    {

    }

    public void Clear()
    {
        screen?.Clear(new Vector4(0, 0, 0, 0));
    }

    public void StartFrame()
    {
        // Headless creates no real native resources, but draining keeps the queue from growing if a
        // test (or a backend-agnostic component) enqueues one, and keeps behaviour uniform.
        Textures.NativeDisposalQueue.Process();
    }

    private DrawNode? rootNode;

    public void SetRoot(DrawNode rootDrawNode)
    {
        rootNode = rootDrawNode;
    }

    public void Resize(int physicalWidth, int physicalHeight, int logicalWidth, int logicalHeight)
    {

    }

    /// <summary>
    /// Walks the draw node tree, as the real backends do, but only when pixel capture is on. With capture
    /// off these stays the no-op it has always been, so no existing test starts rasterize behind its back.
    /// </summary>
    public void Draw(IClock clock)
    {
        if (rasterizer == null || rootNode == null)
            return;

        // Mirrors the real per-frame reset: a frame that aborted mid-offscreen-pass must not leak its
        // bound target into this one.
        boundTargets.Clear();
        SetBlendMode(BlendingMode.Alpha);

        rootNode.Draw(this);
    }

    public void DrawVertices(ReadOnlySpan<Vertex.Vertex> vertices, Texture textureGl)
    {
        // the capture path only understands quads, which is what every
        // compositing path in the framework emits
        DrawQuads(vertices, textureGl);
    }

    public void DrawQuads(ReadOnlySpan<Vertex.Vertex> vertices, Texture textureGl)
    {
        if (rasterizer == null || currentTarget is not { } target)
            return;

        var texture = surfaceOf(textureGl);

        for (int i = 0; i + 4 <= vertices.Length; i += 4)
            rasterizer.DrawQuad(target, vertices.Slice(i, 4), texture, currentBlendMode, currentViewport);
    }

    public void PushMask(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius)
    {
        throw new NotImplementedException();
    }

    public void PopMask(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius, float borderThickness, Color borderColor, ReadOnlySpan<Vertex.Vertex> maskVertices = default)
    {

    }

    public void DrawEdgeEffect(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius, float edgeRadius, Vector2 offset, Color color, bool glow, bool hollow, ReadOnlySpan<Vertex.Vertex> quadVertices)
    {

    }

    public void SetBlendMode(BlendingMode blendingMode)
    {
        currentBlendMode = blendingMode;
    }
    /// <summary>
    /// Runs the action immediately. There is no draw thread and no frame boundary to defer to, and
    /// dropping it would silently skip texture uploads and resource releases — including returning pooled
    /// upload buffers, which are freed by the scheduled work itself.
    /// </summary>
    public void ScheduleToDrawThread(Action action)
    {
        action?.Invoke();
    }

    public void FlushBatch() { }

    public void RestoreMainShader() { }

    public IShader CreateShader(Storage storage, string vertexPath, string fragmentPath) => new HeadlessShader();

    public INativeVideoTexture CreateVideoTexture(int width, int height) => new HeadlessNativeVideoTexture(width, height);

    public INativeTexture CreateNativeTexture(int width, int height) => new HeadlessNativeTexture(width, height);

    public Vector2 RenderScale => Vector2.One;

    public IFrameBuffer CreateFrameBuffer(int width, int height, bool pixelSnapping = false) => new HeadlessFrameBuffer(width, height, PixelCaptureEnabled);

    public void BindFrameBuffer(IFrameBuffer frameBuffer, RectangleF sourceRect, Color clearColor = default)
    {
        if (rasterizer == null)
            return;

        if (surfaceOf(frameBuffer.Texture) is not { } surface)
            return;

        surface.Clear(new Vector4(
            clearColor.R / 255f,
            clearColor.G / 255f,
            clearColor.B / 255f,
            clearColor.A / 255f)
        );

        boundTargets.Add((surface, new HeadlessRasterizer.ViewportTransform(sourceRect, surface.Width, surface.Height)));
    }

    public void UnbindFrameBuffer()
    {
        if (boundTargets.Count > 0)
            boundTargets.RemoveAt(boundTargets.Count - 1);
    }

    /// <summary>
    /// A framebuffer that allocates and releases a color attachment (exactly as the real backends do to
    /// minus the GPU call)
    /// </summary>
    private sealed class HeadlessFrameBuffer : IFrameBuffer
    {
        public Texture Texture { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }

        private readonly bool capturePixels;

        public HeadlessFrameBuffer(int width, int height, bool capturePixels)
        {
            this.capturePixels = capturePixels;
            createAttachment(width, height);
        }

        private void createAttachment(int width, int height)
        {
            Width = Math.Max(1, width);
            Height = Math.Max(1, height);

            releaseAttachment();

            var native = new HeadlessNativeTexture(Width, Height);

            if (capturePixels)
                native.Surface = new PixelSurface(Width, Height);

            Texture = new Texture(native);
        }

        private void releaseAttachment() => Texture?.Dispose();

        public void Resize(int width, int height)
        {
            if (Math.Max(1, width) == Width && Math.Max(1, height) == Height)
                return;

            createAttachment(width, height);
        }

        public void Dispose() => releaseAttachment();
    }
}
