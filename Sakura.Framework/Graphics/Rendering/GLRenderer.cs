// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Sakura.Framework.Graphics.Drawables;
using Silk.NET.OpenGL;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Timing;
using Color = Sakura.Framework.Graphics.Colors.Color;

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

    private readonly float[] _vertices =
    {
        // Positions      // Tex Coords
        0.0f, 1.0f, 0.0f, 0.0f, 1.0f, // Top-left
        1.0f, 1.0f, 0.0f, 1.0f, 1.0f, // Top-right
        1.0f, 0.0f, 0.0f, 1.0f, 0.0f, // Bottom-right
        0.0f, 0.0f, 0.0f, 0.0f, 0.0f, // Bottom-left
    };

    private readonly uint[] _indices =
    {
        0, 1, 2,
        2, 3, 0
    };

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
        fixed(float* v = &_vertices[0])
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(_vertices.Length * sizeof(float)), v, BufferUsageARB.StaticDraw);

        ebo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ebo);
        fixed(uint* i = &_indices[0])
            gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(_indices.Length * sizeof(uint)), i, BufferUsageARB.StaticDraw);

        // Position attribute
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), (void*)0);
        gl.EnableVertexAttribArray(0);

        // Texture coord attribute
        gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), (void*)(3 * sizeof(float)));
        gl.EnableVertexAttribArray(1);

        gl.BindVertexArray(0);
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
        gl.BindVertexArray(0);
    }

    public unsafe void DrawDrawable(Drawable drawable)
    {
        if (drawable.Alpha <= 0) return;
        if (drawable.Texture == null) return;

        drawable.Texture.Bind();

        var color = drawable.Color;
        var finalColor = new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f * drawable.Alpha);

        // This is a placeholder for a proper colour system.
        // It would normally be passed as a vertex attribute for batching.
        gl.ProgramUniform4(shader.Handle, gl.GetUniformLocation(shader.Handle, "aColour"), finalColor);

        shader.SetUniform("u_Model", drawable.ModelMatrix);

        gl.DrawElements(PrimitiveType.Triangles, (uint)_indices.Length, DrawElementsType.UnsignedInt, null);
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
