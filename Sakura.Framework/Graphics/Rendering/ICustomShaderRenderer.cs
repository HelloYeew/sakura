// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Graphics.Rendering;

/// <summary>
/// A renderer that can draw with a custom shader. Draw nodes that bind their own shader and textures
/// (the buffered-container effect passes, video) check for this interface instead of for each backend,
/// and skip the custom pass on a renderer without it (headless).
/// </summary>
public interface ICustomShaderRenderer : IRenderer
{
    /// <summary>
    /// Uploads the given vertices and issues a raw triangle draw using whatever shader is currently
    /// bound, without touching the renderer's texture-slot or clip-injection bookkeeping. The caller
    /// binds its own shader and textures first and calls <see cref="IRenderer.RestoreMainShader"/>
    /// afterwards.
    /// </summary>
    void DrawVerticesRaw(ReadOnlySpan<Vertex.Vertex> vertices);
}
