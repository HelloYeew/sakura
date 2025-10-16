// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering.Batches;
using Sakura.Framework.Graphics.Rendering.Vertex;
using Silk.NET.OpenGL;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Timing;
using Color = Sakura.Framework.Graphics.Colors.Color;
using SakuraVertex = Sakura.Framework.Graphics.Rendering.Vertex.Vertex;

namespace Sakura.Framework.Graphics.Rendering;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class GLRenderer : IRenderer
{
    private static GL gl;
    private Shader shader;
    private uint vao;
    private uint vbo;
    private uint ebo;

    private Matrix4x4 projectionMatrix;

    private IVertexBatch<VertexQuad> quadBatch;

    private Drawable root;

    public unsafe void Initialize(IGraphicsSurface graphicsSurface)
    {
        gl = GL.GetApi(graphicsSurface.GetFunctionAddress);
        if (gl == null)
        {
            throw new InvalidOperationException("Failed to initialize OpenGL context.");
        }

        Logger.Verbose("🖼️ OpenGL renderer initialized");
        byte* glInfo = gl.GetString(StringName.Version);
        Logger.Verbose($"GL Version: {new string((sbyte*)glInfo)}");
        glInfo = gl.GetString(StringName.Renderer);
        Logger.Verbose($"GL Renderer: {new string((sbyte*)glInfo)}");
        glInfo = gl.GetString(StringName.ShadingLanguageVersion);
        Logger.Verbose($"GL Shading Language Version: {new string((sbyte*)glInfo)}");
        glInfo = gl.GetString(StringName.Vendor);
        Logger.Verbose($"GL Vendor: {new string((sbyte*)glInfo)}");
        Logger.Verbose($"GL Extensions: {GetExtensions()}");

        Logger.Verbose("🚅 Hardware Acceleration Information");
        Logger.Verbose($"JIT intrinsic support: {RuntimeInfo.IsIntrinsicSupported}");

        gl.ClearColor(Color.Black);

        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        Textures.Texture.CreateWhitePixel(gl);

        shader = new Shader(gl, "Resources/Shaders/shader.vert", "Resources/Shaders/shader.frag");

        vao = gl.GenVertexArray();
        gl.BindVertexArray(vao);

        vbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        // Use BufferSubData to update this buffer, so we can initialize it with a size.
        gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(1000 * VertexQuad.Size), null, BufferUsageARB.DynamicDraw);

        ebo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ebo);

        uint[] indices = new uint[1000 * 6];
        for (uint i = 0; i < 1000; i++)
        {
            indices[i * 6 + 0] = i * 4 + 0;
            indices[i * 6 + 1] = i * 4 + 1;
            indices[i * 6 + 2] = i * 4 + 2;
            indices[i * 6 + 3] = i * 4 + 2;
            indices[i * 6 + 4] = i * 4 + 3;
            indices[i * 6 + 5] = i * 4 + 0;
        }

        fixed (uint* ptr = indices)
        {
            gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indices.Length * sizeof(uint)), ptr, BufferUsageARB.StaticDraw);
        }

        // Define vertex attributes
        // Position attribute
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, (uint)SakuraVertex.Size, (void*)Marshal.OffsetOf<SakuraVertex>(nameof(SakuraVertex.Position)));

        // Texture coord attribute
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, (uint)SakuraVertex.Size, (void*)Marshal.OffsetOf<SakuraVertex>(nameof(SakuraVertex.TexCoords)));

        // Color attribute
        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, (uint)SakuraVertex.Size, (void*)Marshal.OffsetOf<SakuraVertex>(nameof(SakuraVertex.Color)));

        gl.BindVertexArray(0);

        quadBatch = new QuadBatch<VertexQuad>(gl, vao, vbo, ebo);
    }

    public void SetRoot(Drawable root)
    {
        this.root = root;
    }

    public void Resize(int width, int height)
    {
        gl.Viewport(0, 0, (uint)width, (uint)height);
        projectionMatrix = Matrix4x4.CreateOrthographicOffCenter(0, width, height, 0, -1, 1);
    }

    public void Clear()
    {
        gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
    }

    public void StartFrame()
    {
        // TODO: Implement frame start logic if needed.
    }

    public void Draw(IClock clock)
    {
        if (root == null) return;

        shader.Use();
        shader.SetUniform("u_Projection", projectionMatrix);
        shader.SetUniform("u_Texture", 0);

        gl.BindVertexArray(vao);
        root.Draw(this);
        quadBatch.Draw(); // Ensure the last batch is drawn.
        gl.BindVertexArray(0);
    }

    public unsafe void DrawDrawable(Drawable drawable)
    {
        if (drawable.Alpha <= 0) return;
        if (drawable.Texture == null) return;

        drawable.Texture.Bind();

        // The drawable has already computed its vertex data. Add it to the batch.
        quadBatch.Add(drawable.VertexQuad);
    }

    /// <summary>
    /// Retrieves the list of OpenGL extensions supported by the current context.
    /// </summary>
    /// <returns>A string containing the names of all supported OpenGL extensions, separated by spaces.</returns>
    private unsafe string GetExtensions()
    {
        gl.GetInteger(GetPName.NumExtensions, out int numExtensions);
        var extensionStringBuilder = new StringBuilder();
        for (uint i = 0; i < numExtensions; i++)
        {
            byte* extension = gl.GetString(StringName.Extensions, i);
            if (extension != null)
            {
                extensionStringBuilder.Append($"{new string((sbyte*)extension)} ");
            }
        }
        return extensionStringBuilder.ToString().TrimEnd();
    }
}
