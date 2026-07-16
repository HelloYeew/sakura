// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.InteropServices;
using Sakura.Framework.Maths;
using Sakura.Framework.Statistic;
using Silk.NET.OpenGL;
using SakuraVertex = Sakura.Framework.Graphics.Rendering.Vertex.Vertex;

namespace Sakura.Framework.Graphics.Rendering.Batches;

/// <summary>
/// An indexed batch for rendering quads and triangle lists in submission order.
/// Quads contribute 4 vertices + 6 indices (a 33% vertex-bandwidth saving over raw
/// triangle pairs); arbitrary triangle lists contribute sequential indices.
/// Buffers are orphaned on every flush so the GPU never stalls waiting on a region
/// it is still reading from the previous draw.
/// </summary>
public class TriangleBatch : IDisposable
{
    private static readonly GlobalStatistic<int> stat_buffer_full_flushes = GlobalStatistics.Get<int>("Renderer", "Buffer Full Flushes");
    private static readonly GlobalStatistic<int> stat_draw_calls = GlobalStatistics.Get<int>("Renderer", "Draw Calls");
    private static readonly GlobalStatistic<int> stat_vertices_drawn = GlobalStatistics.Get<int>("Renderer", "Vertices Drawn");

    private readonly GL gl;
    private readonly uint vao;
    private readonly uint vbo;
    private readonly uint ebo;

    private readonly uint quadEbo;

    private bool batchHasNonQuad;

    private readonly SakuraVertex[] vertices;
    private readonly uint[] indices;
    private int vertexCount;
    private int indexCount;
    private readonly int vertexSize;
    private readonly int maxVertices;
    private readonly int maxIndices;

    public unsafe TriangleBatch(GL gl, int maxVertices)
    {
        this.gl = gl;
        vertexSize = Marshal.SizeOf<SakuraVertex>();
        this.maxVertices = maxVertices;
        // Quads use 6 indices per 4 vertices (1.5×); triangle lists use 1 index per vertex.
        maxIndices = maxVertices * 3 / 2;

        vertices = new SakuraVertex[this.maxVertices];
        indices = new uint[maxIndices];

        vao = gl.GenVertexArray();
        vbo = gl.GenBuffer();
        ebo = gl.GenBuffer();
        quadEbo = gl.GenBuffer();

        gl.BindVertexArray(vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);

        // Allocate large, empty buffers on the GPU; refreshed via orphaning each flush.
        gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(maxVertices * vertexSize), null, BufferUsageARB.DynamicDraw);

        // The element buffer binding is captured by the VAO.
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ebo);
        gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(maxIndices * sizeof(uint)), null, BufferUsageARB.DynamicDraw);

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

        // Location 3: Texture Index
        gl.EnableVertexAttribArray(3);
        gl.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, (uint)vertexSize, (void*)Marshal.OffsetOf<SakuraVertex>(nameof(SakuraVertex.TexIndex)));

        // Location 4: Clip Data (Center and Size)
        gl.EnableVertexAttribArray(4);
        gl.VertexAttribPointer(4, 4, VertexAttribPointerType.Float, false, (uint)vertexSize, (void*)Marshal.OffsetOf<SakuraVertex>(nameof(SakuraVertex.ClipData)));

        // Location 5: Clip Shear X
        gl.EnableVertexAttribArray(5);
        gl.VertexAttribPointer(5, 1, VertexAttribPointerType.Float, false, (uint)vertexSize, (void*)Marshal.OffsetOf<SakuraVertex>(nameof(SakuraVertex.ClipShearX)));

        // Location 6: Clip Radius
        gl.EnableVertexAttribArray(6);
        gl.VertexAttribPointer(6, 1, VertexAttribPointerType.Float, false, (uint)vertexSize, (void*)Marshal.OffsetOf<SakuraVertex>(nameof(SakuraVertex.ClipRadius)));

        gl.BindVertexArray(0);

        // Fill the static quad index buffer once. Done with no VAO bound so it doesn't disturb the
        // VAO's element-buffer binding, Draw() explicitly binds whichever EBO it needs each flush.
        int maxQuads = maxVertices / 4;
        var quadIndices = new uint[maxQuads * 6];
        for (int q = 0; q < maxQuads; q++)
        {
            uint baseIndex = (uint)(q * 4);
            int o = q * 6;
            quadIndices[o + 0] = baseIndex;
            quadIndices[o + 1] = baseIndex + 1;
            quadIndices[o + 2] = baseIndex + 2;
            quadIndices[o + 3] = baseIndex + 2;
            quadIndices[o + 4] = baseIndex + 3;
            quadIndices[o + 5] = baseIndex;
        }

        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, quadEbo);
        fixed (uint* ptr = quadIndices)
            gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(quadIndices.Length * sizeof(uint)), ptr, BufferUsageARB.StaticDraw);
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, 0);
    }

    private void ensureCapacity(int vertexSpace, int indexSpace)
    {
        if (vertexCount + vertexSpace > maxVertices || indexCount + indexSpace > maxIndices)
        {
            stat_buffer_full_flushes.Value++;
            Draw();
        }
    }

    /// <summary>
    /// Adds a quad of exactly 4 vertices ordered top-left, top-right, bottom-right, bottom-left.
    /// </summary>
    public void AddQuad(ReadOnlySpan<SakuraVertex> quad, float textureIndex = 0f, Vector4? clipData = null, float clipShearX = 0f, float clipRadius = 0f)
    {
        ensureCapacity(4, 6);

        Vector4 actualClipData = clipData ?? new Vector4(0, 0, -1, -1);

        uint baseIndex = (uint)vertexCount;

        for (int i = 0; i < 4; i++)
        {
            var v = quad[i];
            v.TexIndex = textureIndex;
            v.ClipData = actualClipData;
            v.ClipShearX = clipShearX;
            v.ClipRadius = clipRadius;
            vertices[vertexCount++] = v;
        }

        indices[indexCount++] = baseIndex;
        indices[indexCount++] = baseIndex + 1;
        indices[indexCount++] = baseIndex + 2;
        indices[indexCount++] = baseIndex + 2;
        indices[indexCount++] = baseIndex + 3;
        indices[indexCount++] = baseIndex;
    }

    /// <summary>
    /// Adds an arbitrary triangle list (sequentially indexed).
    /// </summary>
    public void AddRange(ReadOnlySpan<SakuraVertex> newVertices, float textureIndex = 0f, Vector4? clipData = null, float clipShearX = 0f, float clipRadius = 0f)
    {
        // Triangle-list vertices break the quad alignment, so this flush must use the dynamic index buffer.
        batchHasNonQuad = true;

        // If no clip rect is provided, use an invalid rect so let shader ignore it
        Vector4 actualClipData = clipData ?? new Vector4(0, 0, -1, -1);

        foreach (var vertex in newVertices)
        {
            ensureCapacity(1, 1);

            var v = vertex;
            v.TexIndex = textureIndex;
            v.ClipData = actualClipData;
            v.ClipShearX = clipShearX;
            v.ClipRadius = clipRadius;

            indices[indexCount++] = (uint)vertexCount;
            vertices[vertexCount++] = v;
        }
    }

    /// <summary>
    /// Uploads the provided vertices directly into the VBO and issues a non-indexed
    /// DrawArrays call without touching any texture slots or the internal batch state.
    /// Used for custom-shader drawables (e.g. VideoDrawNode) that manage their own textures.
    /// </summary>
    public unsafe void DrawRaw(ReadOnlySpan<SakuraVertex> rawVertices)
    {
        if (rawVertices.IsEmpty) return;

        gl.BindVertexArray(vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);

        // Orphan before overwriting: the GPU may still be reading the previous contents.
        gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(maxVertices * vertexSize), null, BufferUsageARB.DynamicDraw);

        fixed (SakuraVertex* ptr = rawVertices)
        {
            gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(rawVertices.Length * vertexSize), ptr);
        }

        gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)rawVertices.Length);

        stat_draw_calls.Value++;
        stat_vertices_drawn.Value += rawVertices.Length;
    }

    /// <summary>
    /// Uploads the current batch to the GPU (orphaning the previous buffer storage)
    /// and draws it with a single indexed draw call.
    /// </summary>
    public unsafe int Draw()
    {
        if (indexCount == 0)
        {
            return 0;
        }

        gl.BindVertexArray(vao);

        // Orphaning: re-specifying the buffer storage lets the driver hand us fresh memory
        // while the GPU finishes reading the old storage, avoiding an implicit sync stall
        // when the batch is flushed multiple times per frame.
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(maxVertices * vertexSize), null, BufferUsageARB.DynamicDraw);

        fixed (SakuraVertex* ptr = vertices)
        {
            gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(vertexCount * vertexSize), ptr);
        }

        if (batchHasNonQuad)
        {
            // upload the CPU-built indices into the dynamic buffer.
            gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ebo);
            gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(maxIndices * sizeof(uint)), null, BufferUsageARB.DynamicDraw);

            fixed (uint* ptr = indices)
            {
                gl.BufferSubData(BufferTargetARB.ElementArrayBuffer, 0, (nuint)(indexCount * sizeof(uint)), ptr);
            }
        }
        else
        {
            // the static quad index buffer already holds the exact pattern for
            // indices [0, indexCount) — no per-flush index upload needed.
            gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, quadEbo);
        }

        gl.DrawElements(PrimitiveType.Triangles, (uint)indexCount, DrawElementsType.UnsignedInt, null);

        stat_draw_calls.Value++;
        stat_vertices_drawn.Value += vertexCount;

        int count = vertexCount;
        vertexCount = 0;
        indexCount = 0;
        batchHasNonQuad = false;
        return count;
    }

    private bool disposed;

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        gl.DeleteVertexArray(vao);
        gl.DeleteBuffer(vbo);
        gl.DeleteBuffer(ebo);
        gl.DeleteBuffer(quadEbo);
        GC.SuppressFinalize(this);
    }
}
