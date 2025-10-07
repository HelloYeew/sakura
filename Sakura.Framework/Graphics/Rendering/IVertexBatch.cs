// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Drawables;

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
    /// Add a <see cref="Drawable"/>'s vertices to this batch.
    /// </summary>
    /// <param name="drawable">The drawable to add.</param>
    void Add(Drawable drawable);
}
