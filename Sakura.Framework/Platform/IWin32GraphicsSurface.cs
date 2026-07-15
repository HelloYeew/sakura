// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Platform;

/// <summary>
/// Extended surface contract for Direct3D 11 rendering
/// </summary>
public interface IWin32GraphicsSurface : IGraphicsSurface
{
    /// <summary>
    /// The native Win32 window handle (HWND) associated with this window.
    /// Obtained via <c>SDL_GetPointerProperty(SDL_PROP_WINDOW_WIN32_HWND_POINTER)</c>.
    /// </summary>
    nint WindowHandle { get; }
}
