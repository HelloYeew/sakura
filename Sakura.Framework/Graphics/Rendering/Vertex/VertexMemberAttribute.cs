// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Graphics.Rendering.Vertex;

/// <summary>
/// Describes a single attribute member of a vertex struct.
/// Each renderer reads this and maps to its own attribute API.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public class VertexMemberAttribute : Attribute
{
    /// <summary>
    /// Number of components (e.g. 2 for a vec2, 4 for a vec4).
    /// </summary>
    public int Count { get; }

    /// <summary>
    /// Primitive data type of each component.
    /// </summary>
    public VertexMemberType Type { get; }

    public VertexMemberAttribute(int count, VertexMemberType type)
    {
        Count = count;
        Type = type;
    }
}
