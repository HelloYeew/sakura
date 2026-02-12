// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Platform;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Graphics.Rendering;

public class HeadlessRenderer : IRenderer
{
    public Texture WhitePixel { get; }
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

    public void SetRoot(Drawable root)
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

    public void DrawCircle(Drawable circleDrawable)
    {

    }

    public void PushMask(Drawable maskDrawable, float cornerRadius)
    {

    }

    public void PopMask(Drawable maskDrawable, float cornerRadius, float borderThickness, Color borderColor)
    {

    }
}
