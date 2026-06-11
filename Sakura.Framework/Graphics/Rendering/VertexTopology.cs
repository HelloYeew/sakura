// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Graphics.Rendering;

/// <summary>
/// Describes how the renderer interprets a drawable's vertex array.
/// </summary>
public enum VertexTopology
{
    /// <summary>
    /// The vertices form a raw triangle list (every 3 vertices = one triangle).
    /// </summary>
    Triangles,

    /// <summary>
    /// The vertices form quads of 4 (ordered top-left, top-right, bottom-right, bottom-left),
    /// rendered through a shared index pattern, use around 33% less vertex bandwidth than triangle pairs.
    /// </summary>
    Quads
}
