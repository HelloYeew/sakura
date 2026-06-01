// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Graphics.Rendering;

public class HeadlessRenderer : IRenderer
{
    public Texture WhitePixel { get; }
    public Sakura.Framework.Maths.Matrix4x4 ProjectionMatrix => default;
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
    public void DrawVerticesRaw(ReadOnlySpan<Vertex.Vertex> vertices) { }
    public void DisableSrgb() { }
    public void RestoreSrgb() { }
}
