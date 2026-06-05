// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Runtime.InteropServices;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Rendering.Vertex;

/// <summary>
/// A vertex with position, color, and texture coordinates.
/// Each renderer maps the <see cref="VertexMemberAttribute"/> annotations to its own attribute layout API.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Vertex
{
    /// <summary>
    /// The position of the vertex.
    /// </summary>
    [VertexMember(2, VertexMemberType.Float)]
    public Vector2 Position;

    /// <summary>
    /// The color of the vertex.
    /// </summary>
    [VertexMember(4, VertexMemberType.Float)]
    public Vector4 Color;

    /// <summary>
    /// The texture coordinates of the vertex.
    /// </summary>
    [VertexMember(2, VertexMemberType.Float)]
    public Vector2 TexCoords;

    /// <summary>
    /// Index of the texture to use, allowing multiple textures per draw call.
    /// </summary>
    [VertexMember(1, VertexMemberType.Float)]
    public float TexIndex;

    /// <summary>
    /// The clipping data (Center.X, Center.Y, HalfWidth, HalfHeight).
    /// </summary>
    [VertexMember(4, VertexMemberType.Float)]
    public Vector4 ClipData;

    /// <summary>
    /// Horizontal shear modifier for clipping.
    /// </summary>
    [VertexMember(1, VertexMemberType.Float)]
    public float ClipShearX;

    /// <summary>
    /// Corner radius for clipping. 0 means no rounding.
    /// </summary>
    [VertexMember(1, VertexMemberType.Float)]
    public float ClipRadius;

    /// <summary>
    /// The size of the Vertex struct in bytes.
    /// </summary>
    public static readonly int Size = Marshal.SizeOf<Vertex>();
}
