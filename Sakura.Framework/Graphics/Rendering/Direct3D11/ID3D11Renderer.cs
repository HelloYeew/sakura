// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Graphics.Rendering.Direct3D11;

/// <summary>
/// Direct3D 11-specific renderer extensions.
/// Only cast to this interface from code that is already D3D11-aware (e.g. the buffered-container
/// effect passes, which need a raw, slot-management-free draw)
/// </summary>
public interface ID3D11Renderer : IRenderer
{
    /// <summary>
    /// Uploads the given vertices and issues a raw triangle draw using whatever shader/pipeline is
    /// currently bound, without touching the renderer's texture-slot or clip-injection bookkeeping.
    /// Used by the buffered-container effect passes, which bind their own effect shader + source
    /// texture.
    /// </summary>
    void DrawVerticesRaw(ReadOnlySpan<Vertex.Vertex> vertices);
}
