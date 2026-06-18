// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Graphics.Rendering.Metal;

/// <summary>
/// Metal-specific renderer extensions
/// Only cast to this interface from code that is already Metal-aware (e.g. the buffered-container
/// effect passes, which need a raw, slot-management-free draw).
/// </summary>
public interface IMetalRenderer : IRenderer
{
    /// <summary>
    /// Uploads the given vertices and issues a raw triangle draw using whatever shader/pipeline is
    /// currently bound, without touching the renderer's texture-slot or clip-injection bookkeeping.
    /// Used by the buffered-container effect passes, which bind their own effect shader + source
    /// texture. Mirrors <see cref="IGLRenderer.DrawVerticesRaw"/>.
    /// </summary>
    void DrawVerticesRaw(ReadOnlySpan<Vertex.Vertex> vertices);
}
