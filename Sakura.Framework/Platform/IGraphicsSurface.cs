// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Platform;

/// <summary>
/// Minimal contract that every graphics surface exposes.
/// The renderer reads <see cref="Type"/> and casts to the matching
/// sub-interface (<see cref="IOpenGLGraphicsSurface"/>, or the future
/// <c>IMetalGraphicsSurface</c>) to get the API-specific handles it needs.
/// </summary>
public interface IGraphicsSurface
{
    /// <summary>
    /// The graphics API this surface exposes.
    /// </summary>
    GraphicsSurfaceType Type { get; }
}
