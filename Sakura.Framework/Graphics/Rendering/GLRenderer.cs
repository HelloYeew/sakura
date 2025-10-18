// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering.Batches;
using Silk.NET.OpenGL;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Timing;
using Color = Sakura.Framework.Graphics.Colors.Color;
using SakuraVertex = Sakura.Framework.Graphics.Rendering.Vertex.Vertex;
using Texture = Sakura.Framework.Graphics.Textures.Texture;

namespace Sakura.Framework.Graphics.Rendering;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class GLRenderer : IRenderer
{
    private static GL gl;
    private Shader shader;

    private Matrix4x4 projectionMatrix;

    private TriangleBatch triangleBatch;

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

        triangleBatch = new TriangleBatch(gl, 1000 * 3);
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

        root.Draw(this);
        triangleBatch.Draw();
    }

    public void DrawVertices(ReadOnlySpan<SakuraVertex> vertices, Texture texture)
    {
        texture.Bind();
        triangleBatch.AddRange(vertices);
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
