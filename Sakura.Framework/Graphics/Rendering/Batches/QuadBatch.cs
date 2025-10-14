// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Rendering.Vertex;
using Silk.NET.OpenGL;

namespace Sakura.Framework.Graphics.Rendering.Batches;

/// <summary>
/// A batch of quads to be drawn together.
/// </summary>
/// <typeparam name="T">The type of vertex quad.</typeparam>
public class QuadBatch<T> : IVertexBatch<T> where T : unmanaged, IVertexQuad
{
    private readonly GL gl;
    private readonly uint vao;
    private readonly uint vbo;
    private readonly uint ebo;

    private readonly T[] quads;
    private int quadCount;

    private const int max_quads = 1000;

    public QuadBatch(GL gl, uint vao, uint vbo, uint ebo)
    {
        this.gl = gl;
        this.vao = vao;
        this.vbo = vbo;
        this.ebo = ebo;
        quads = new T[max_quads];
    }

    public void Add(T data)
    {
        if (quadCount >= max_quads)
            return;

        quads[quadCount++] = data;
    }

    public unsafe int Draw()
    {
        if (quadCount == 0)
            return 0;

        gl.BindVertexArray(vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);

        fixed (T* ptr = quads)
        {
            gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(quadCount * sizeof(T)), ptr);
        }

        gl.DrawElements(PrimitiveType.Triangles, (uint)(quadCount * 6), DrawElementsType.UnsignedInt, null);

        int count = quadCount;
        quadCount = 0;
        return count;
    }
}
