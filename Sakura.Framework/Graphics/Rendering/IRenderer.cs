// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Platform;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Graphics.Rendering;

public interface IRenderer
{
    /// <summary>
    /// Initializes the renderer to be used with the specified window.
    /// </summary>
    protected internal void Initialize(IGraphicsSurface graphicsSurface);

    void Clear();

    void StartFrame();

    void SetRoot(Drawable root);

    void Resize(int width, int height);

    void Draw(IClock clock);

    void DrawVertices(ReadOnlySpan<Vertex.Vertex> vertices, Texture texture);

    void PushMask(Drawable maskDrawable, float cornerRadius);

    void PopMask(Drawable maskDrawable, float cornerRadius);
}
