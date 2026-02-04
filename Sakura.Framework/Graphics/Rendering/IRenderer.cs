// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Platform;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Graphics.Rendering;

public interface IRenderer
{
    /// <summary>
    /// A 1x1 white pixel texture managed by the renderer.
    /// </summary>
    Texture WhitePixel { get; }

    /// <summary>
    /// Initializes the renderer to be used with the specified window.
    /// </summary>
    protected internal void Initialize(IGraphicsSurface graphicsSurface);

    void Clear();

    void StartFrame();

    void SetRoot(Drawable root);

    void Resize(int physicalWidth, int physicalHeight, int logicalWidth, int logicalHeight);

    void Draw(IClock clock);

    void DrawVertices(ReadOnlySpan<Vertex.Vertex> vertices, Texture textureGl);

    void DrawCircle(Drawable circleDrawable);

    void PushMask(Drawable maskDrawable, float cornerRadius);

    void PopMask(Drawable maskDrawable, float cornerRadius, float borderThickness, Color borderColor);
}
