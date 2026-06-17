// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
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

    public void Initialize(IGraphicsSurface graphicsSurface)
    {

    }

    public void Clear()
    {

    }

    public void StartFrame()
    {

    }

    public void SetRoot(DrawNode rootNode)
    {

    }

    public void Resize(int physicalWidth, int physicalHeight, int logicalWidth, int logicalHeight)
    {

    }

    public void Draw(IClock clock)
    {

    }

    public void DrawVertices(ReadOnlySpan<Vertex.Vertex> vertices, Texture textureGl)
    {

    }

    public void DrawQuads(ReadOnlySpan<Vertex.Vertex> vertices, Texture textureGl)
    {

    }

    public void PushMask(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius)
    {
        throw new NotImplementedException();
    }

    public void PopMask(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius, float borderThickness, Color borderColor, ReadOnlySpan<Vertex.Vertex> maskVertices = default)
    {

    }

    public void SetBlendMode(BlendingMode blendingMode)
    {

    }
    public void ScheduleToDrawThread(Action action)
    {

    }

    public void FlushBatch() { }

    public void RestoreMainShader() { }

    public IShader CreateShader(Storage storage, string vertexPath, string fragmentPath) => new HeadlessShader();

    public INativeVideoTexture CreateVideoTexture(int width, int height) => new HeadlessNativeVideoTexture(width, height);

    public Vector2 RenderScale => Vector2.One;

    public IFrameBuffer CreateFrameBuffer(int width, int height, bool pixelSnapping = false) => new HeadlessFrameBuffer(WhitePixel, width, height);

    public void BindFrameBuffer(IFrameBuffer frameBuffer, RectangleF sourceRect, Color clearColour = default)
    {

    }

    public void UnbindFrameBuffer()
    {

    }

    private sealed class HeadlessFrameBuffer : IFrameBuffer
    {
        public Texture Texture { get; }
        public int Width { get; private set; }
        public int Height { get; private set; }

        public HeadlessFrameBuffer(Texture texture, int width, int height)
        {
            Texture = texture;
            Width = width;
            Height = height;
        }

        public void Resize(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public void Dispose()
        {
        }
    }
}
