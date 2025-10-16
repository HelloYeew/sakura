// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Rendering.Vertex;

namespace Sakura.Framework.Graphics.Rendering;

/// <summary>
/// A batch of vertices to be rendered together.
/// </summary>
public interface IVertexBatch<in T> where T : struct, IVertexQuad
{
    /// <summary>
    /// The number of vertices to draw.
    /// </summary>
    /// <returns></returns>
    int Draw();

    /// <summary>
    /// Draws the batch and returns the number of vertices drawn.
    /// </summary>
    /// <returns>The number of vertices drawn.</returns>
    void Add(T data);
}
