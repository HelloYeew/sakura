// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Platform;

/// <summary>
/// <see cref="IWin32GraphicsSurface"/> implementation backed by an SDL3 window.
/// The <see cref="WindowHandle"/> pointer is set by <see cref="SDLWindow.InitializeWin32Surface"/>.
/// </summary>
public class Win32GraphicsSurface : IWin32GraphicsSurface
{
    public GraphicsSurfaceType Type => GraphicsSurfaceType.Direct3D11;

    public nint WindowHandle { get; internal set; }
}
