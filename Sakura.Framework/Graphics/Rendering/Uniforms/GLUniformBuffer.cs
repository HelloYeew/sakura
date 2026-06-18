// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Silk.NET.OpenGL;

namespace Sakura.Framework.Graphics.Rendering.Uniforms;

/// <summary>
/// A GL uniform buffer object holding a single std140-laid-out block of type
/// <typeparamref name="T"/>, bound to a fixed binding point.
/// </summary>
public sealed class GLUniformBuffer<T> : IDisposable
    where T : unmanaged
{
    /// <summary>The binding point this buffer is bound to (matches the shader's <c>binding=</c>).</summary>
    public uint BindingPoint { get; }

    private readonly GL gl;
    private readonly uint handle;
    private readonly int size;
    private bool allocated;
    private bool disposed;

    public GLUniformBuffer(GL gl, uint bindingPoint)
    {
        this.gl = gl;
        BindingPoint = bindingPoint;
        unsafe { size = sizeof(T); }
        handle = gl.GenBuffer();
    }

    /// <summary>
    /// Uploads <paramref name="data"/> to the GPU. The first call allocates storage with
    /// <c>glBufferData</c>, subsequent calls reuse it with <c>glBufferSubData</c>.
    /// </summary>
    public unsafe void Update(in T data)
    {
        gl.BindBuffer(BufferTargetARB.UniformBuffer, handle);

        fixed (T* ptr = &data)
        {
            if (!allocated)
            {
                gl.BufferData(BufferTargetARB.UniformBuffer, (nuint)size, ptr, BufferUsageARB.DynamicDraw);
                allocated = true;
            }
            else
            {
                gl.BufferSubData(BufferTargetARB.UniformBuffer, 0, (nuint)size, ptr);
            }
        }

        gl.BindBuffer(BufferTargetARB.UniformBuffer, 0);
    }

    /// <summary>
    /// Binds this buffer to its binding point so shaders whose matching block is linked to the
    /// same point read from it. Safe to call every frame.
    /// </summary>
    public void Bind()
    {
        gl.BindBufferBase(BufferTargetARB.UniformBuffer, BindingPoint, handle);
    }

    public void Dispose()
    {
        if (disposed) return;
        gl.DeleteBuffer(handle);
        disposed = true;
    }
}
