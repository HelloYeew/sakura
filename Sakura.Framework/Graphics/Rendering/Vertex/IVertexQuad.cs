// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Rendering.Vertex;

/// <summary>
/// A quad that can be drawn.
/// </summary>
public interface IVertexQuad
{
    /// <summary>
    /// The vertices of the quad.
    /// </summary>
    Vertex[] Vertices { get; }

    /// <summary>
    /// The screen space quad of the quad.
    /// </summary>
    Quad ScreenSpaceQuad { get; }

    /// <summary>
    /// The texture coordinates of the quad.
    /// </summary>
    Vector2[] TexCoords { get; }
}
