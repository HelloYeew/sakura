// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Platform;

namespace Sakura.Framework.Graphics.Rendering;

public interface IRenderer
{
    /// <summary>
    /// Initializes the renderer to be used with the specified window.
    /// </summary>
    protected internal void Initialize(IGraphicsSurface graphicsSurface);
}
