// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.InteropServices;
using Sakura.Framework.Statistic;
using Silk.NET.OpenGL;
using SakuraVertex = Sakura.Framework.Graphics.Rendering.Vertex.Vertex;

namespace Sakura.Framework.Graphics.Rendering.Batches;

/// <summary>
/// A batch for rendering a stream of vertices as triangles.
/// This batch is renderer-agnostic and manages its own buffers.
/// </summary>
public class TriangleBatch
{
    private readonly GL gl;
    private readonly uint vao;
    private readonly uint vbo;

    private readonly SakuraVertex[] vertices;
    private int vertexCount;
    private readonly int vertexSize;
    private readonly int maxVertices;

    public unsafe TriangleBatch(GL gl, int maxVertices)
    {
        this.gl = gl;
        vertexSize = Marshal.SizeOf<SakuraVertex>();
        this.maxVertices = maxVertices;
        vertices = new SakuraVertex[this.maxVertices];

        vao = gl.GenVertexArray();
        vbo = gl.GenBuffer();

        gl.BindVertexArray(vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);

        // Allocate a large, empty buffer on the GPU, will update it with BufferSubData.
        gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(maxVertices * vertexSize), null, BufferUsageARB.DynamicDraw);

        // Define vertex attributes for the Vertex struct.
        // This must match the layout in the vertex shader.
        // Location 0: Position
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, (uint)vertexSize, (void*)Marshal.OffsetOf<SakuraVertex>(nameof(SakuraVertex.Position)));

        // Location 1: Texture Coordinates
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, (uint)vertexSize, (void*)Marshal.OffsetOf<SakuraVertex>(nameof(SakuraVertex.TexCoords)));

        // Location 2: Color
        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, (uint)vertexSize, (void*)Marshal.OffsetOf<SakuraVertex>(nameof(SakuraVertex.Color)));

        gl.BindVertexArray(0);
    }

    public void Add(in SakuraVertex vertex)
    {
        // If the batch is full, automatically flush it to make room.
        if (vertexCount >= maxVertices)
        {
            Draw();
        }
        vertices[vertexCount++] = vertex;
    }

    public void AddRange(ReadOnlySpan<SakuraVertex> newVertices)
    {
        foreach (var vertex in newVertices)
        {
            Add(vertex);
        }
    }

    /// <summary>
    /// Uploads the current batch of vertices to the GPU and draws them.
    /// </summary>
    public unsafe int Draw()
    {
        if (vertexCount == 0)
        {
            return 0;
        }

        gl.BindVertexArray(vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);

        // Upload the vertex data from our CPU-side array to the GPU's VBO.
        fixed (SakuraVertex* ptr = vertices)
        {
            gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(vertexCount * vertexSize), ptr);
        }

        // Issue the draw call to render the vertices as triangles.
        gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)vertexCount);

        GlobalStatistics.Get<int>("Renderer", "Draw Calls").Value++;
        GlobalStatistics.Get<int>("Renderer", "Vertices Drawn").Value += vertexCount;

        int count = vertexCount;
        vertexCount = 0; // Reset for the next frame.
        return count;
    }
}
