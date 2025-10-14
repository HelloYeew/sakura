// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Runtime.InteropServices;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Rendering.Vertex;

/// <summary>
/// A quad compound of four vertices.
/// This struct design to be sent directly to the GPU.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct VertexQuad : IVertexQuad
{
    /// <summary>
    /// The top-left vertex of the quad.
    /// </summary>
    public Vertex TopLeft;

    /// <summary>
    /// The top-right vertex of the quad.
    /// </summary>
    public Vertex TopRight;

    /// <summary>
    /// The bottom-right vertex of the quad.
    /// </summary>
    public Vertex BottomRight;

    /// <summary>
    /// The bottom-left vertex of the quad.
    /// </summary>
    public Vertex BottomLeft;

    /// <summary>
    /// The size of the <see cref="VertexQuad"/> struct in bytes.
    /// </summary>
    public static readonly int Size = Marshal.SizeOf<VertexQuad>();

    /// <summary>
    /// Gets an array containing the four vertices of the quad in clockwise order starting from the top-left vertex.
    /// </summary>
    public Vertex[] Vertices => new[] { TopLeft, TopRight, BottomRight, BottomLeft };

    /// <summary>
    /// Get the screen space quad of the vertex quad.
    /// </summary>
    public Quad ScreenSpaceQuad => new Quad(
        TopLeft.Position,
        TopRight.Position,
        BottomRight.Position,
        BottomLeft.Position
    );

    /// <summary>
    /// Get the default texture coordinates of the quad.
    /// </summary>
    public Vector2[] TexCoords => new[]
    {
        new Vector2(0, 1),
        new Vector2(1, 1),
        new Vector2(1, 0),
        new Vector2(0, 0)
    };
}
