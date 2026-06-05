// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Platform;

/// <summary>
/// <see cref="IMetalGraphicsSurface"/> implementation backed by an SDL2 window.
/// The <see cref="MetalLayer"/> pointer is set by <see cref="SDLWindow.InitializeMetalSurface"/>.
/// </summary>
public class MetalGraphicsSurface : IMetalGraphicsSurface
{
    public GraphicsSurfaceType Type => GraphicsSurfaceType.Metal;

    public nint MetalLayer { get; internal set; }
}
