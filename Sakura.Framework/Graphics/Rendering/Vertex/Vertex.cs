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
    /// The size of the Vertex struct in bytes.
    /// </summary>
    public static readonly int Size = Marshal.SizeOf<Vertex>();
}
