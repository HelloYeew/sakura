// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Runtime.InteropServices;
using Sakura.Framework.Maths;
using Silk.NET.OpenGL;

namespace Sakura.Framework.Graphics.Rendering.Vertex;

/// <summary>
/// A vertex with position, color, and texture coordinates.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Vertex
{
    /// <summary>
    /// The position of the vertex.
    /// </summary>
    [VertexMember(2, VertexAttribPointerType.Float)]
    public Vector2 Position;

    /// <summary>
    /// The color of the vertex.
    /// </summary>
    [VertexMember(4, VertexAttribPointerType.Float)]
    public Vector4 Color;

    /// <summary>
    /// The texture coordinates of the vertex.
    /// </summary>
    [VertexMember(2, VertexAttribPointerType.Float)]
    public Vector2 TexCoords;

    /// <summary>
    /// The index of the texture to use for this vertex to make it able to bind multiple textures in a single draw call.
    /// </summary>
    [VertexMember(1, VertexAttribPointerType.Float)]
    public float TexIndex;

    /// <summary>
    /// The clipping rectangle for this vertex, used for software clipping in the shader.
    /// The rectangle is defined as (Left, Top, Right, Bottom).
    /// </summary>
    [VertexMember(4, VertexAttribPointerType.Float)]
    public Vector4 ClipRect;

    /// <summary>
    /// The radius for clipping corners, used for rounded rectangles. A value of 0 means no rounding.
    /// </summary>
    [VertexMember(1, VertexAttribPointerType.Float)]
    public float ClipRadius;

    /// <summary>
    /// The size of the Vertex struct in bytes.
    /// </summary>
    public static readonly int Size = Marshal.SizeOf<Vertex>();
}
