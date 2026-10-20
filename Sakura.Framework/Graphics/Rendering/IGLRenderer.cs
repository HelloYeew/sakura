// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Diagnostics.CodeAnalysis;

namespace Sakura.Framework.Graphics.Rendering;

/// <summary>
/// OpenGL-specific renderer extensions.
/// Only cast to this interface from code that is already GL-specific.
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
public interface IGLRenderer : ICustomShaderRenderer
{
    /// <summary>
    /// Disables sRGB framebuffer encoding for the next draw call.
    /// Required for video rendering: YUV→RGB output is already gamma-encoded
    /// and must not be re-encoded by the sRGB framebuffer path.
    /// Call <see cref="RestoreSrgb"/> immediately after the draw call.
    /// </summary>
    void DisableSrgb();

    /// <summary>
    /// Re-enables sRGB framebuffer encoding after a <see cref="DisableSrgb"/> call.
    /// </summary>
    void RestoreSrgb();
}
