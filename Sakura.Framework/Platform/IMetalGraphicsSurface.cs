// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Platform;

/// <summary>
/// Extended surface contract for Metal rendering.
/// <c>MetalRenderer</c> casts <see cref="IGraphicsSurface"/> to this interface during
/// <c>Initialize</c> to obtain the native <c>CAMetalLayer</c> pointer.
/// </summary>
public interface IMetalGraphicsSurface : IGraphicsSurface
{
    /// <summary>
    /// Pointer to the native <c>CAMetalLayer</c> associated with this window.
    /// Obtained via <c>SDL_Metal_CreateView</c> + <c>SDL_Metal_GetLayer</c>.
    /// </summary>
    nint MetalLayer { get; }
}
