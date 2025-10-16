// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Silk.NET.OpenGL;

namespace Sakura.Framework.Graphics.Rendering.Vertex;

/// <summary>
/// Attribute to describe a member of a vertex struct.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public class VertexMemberAttribute : Attribute
{
    public int Count { get; }
    public VertexAttribPointerType Type { get; }

    public VertexMemberAttribute(int count, VertexAttribPointerType type)
    {
        Count = count;
        Type = type;
    }
}
