// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Graphics.Rendering;

/// <summary>
/// A batch of vertices to be rendered together.
/// </summary>
public interface IVertexBatch
{
    /// <summary>
    /// The number of vertices to draw.
    /// </summary>
    /// <returns></returns>
    int Draw();

    /// <summary>
    /// Adds a vertex to the batch.
    /// </summary>
    void Add(in Vertex.Vertex vertex);

    /// <summary>
    /// Adds a range of vertices to the batch.
    /// </summary>
    /// <param name="vertices"></param>
    void AddRange(ReadOnlySpan<Vertex.Vertex> vertices);
}
