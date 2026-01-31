// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering.Batches;
using Sakura.Framework.Graphics.Textures;
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

    internal static GL GL => gl;

    public Texture WhitePixel { get; private set; }

    private Shader shader;

    private Matrix4x4 projectionMatrix;

    private TriangleBatch triangleBatch;

    private Drawable root;

    private int stencilLevel = 0;

    private uint lastBoundTextureHandle = uint.MaxValue;

    private int viewportHeight;
    private readonly Stack<Vector4> scissorStack = new Stack<Vector4>();

    private float renderScaleX = 1.0f;
    private float renderScaleY = 1.0f;

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

        gl.Enable(EnableCap.FramebufferSrgb);

        gl.Enable(EnableCap.StencilTest);
        gl.StencilMask(0xFF);
        gl.ClearStencil(0);
        gl.Clear(ClearBufferMask.StencilBufferBit);
        gl.StencilMask(0x00);

        gl.Disable(EnableCap.ScissorTest);

        GLTexture.CreateWhitePixel(gl);
        WhitePixel = new Texture(GLTexture.WhitePixel);

        shader = new Shader(gl, "Resources/Shaders/shader.vert", "Resources/Shaders/shader.frag");

        triangleBatch = new TriangleBatch(gl, 1000 * 3);

        lastBoundTextureHandle = uint.MaxValue;
    }

    public void SetRoot(Drawable drawableRoot)
    {
        root = drawableRoot;
    }

    public void Resize(int physicalWidth, int physicalHeight, int logicalWidth, int logicalHeight)
    {
        gl.Viewport(0, 0, (uint)physicalWidth, (uint)physicalHeight);
        viewportHeight = physicalHeight;
        projectionMatrix = Matrix4x4.CreateOrthographicOffCenter(0, logicalWidth, logicalHeight, 0, -1, 1);
        renderScaleX = (float)physicalWidth / logicalWidth;
        renderScaleY = (float)physicalHeight / logicalHeight;
    }

    public void Clear()
    {
        gl.Disable(EnableCap.ScissorTest);
        gl.StencilMask(0xFF);
        gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
        gl.StencilMask(0x00);
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

        stencilLevel = 0;
        scissorStack.Clear();
        gl.Disable(EnableCap.ScissorTest);
        gl.StencilMask(0x00);
        gl.StencilFunc(StencilFunction.Always, 0, 0xFF);
        gl.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);
        shader.SetUniform("u_IsMasking", false);
        shader.SetUniform("u_IsCircle", false);
        shader.SetUniform("u_IsBorder", false);

        lastBoundTextureHandle = uint.MaxValue;

        root.Draw(this);
        triangleBatch.Draw();
    }

    public void DrawVertices(ReadOnlySpan<SakuraVertex> vertices, Texture texture)
    {
        uint newTextureHandle = texture.GlTexture.Handle;

        if (newTextureHandle != lastBoundTextureHandle)
        {
            triangleBatch.Draw();
            texture.GlTexture.Bind();
            lastBoundTextureHandle = newTextureHandle;
        }

        triangleBatch.AddRange(vertices);
    }

    public void DrawCircle(Drawable circleDrawable)
    {
        triangleBatch.Draw();

        shader.SetUniform("u_IsCircle", true);
        var rect = circleDrawable.DrawRectangle;
        shader.SetUniform("u_CircleRect", new Vector4(rect.X, rect.Y, rect.Width, rect.Height));

        if (GLTexture.WhitePixel.Handle != lastBoundTextureHandle)
        {
            triangleBatch.Draw();
            GLTexture.WhitePixel.Bind();
            lastBoundTextureHandle = GLTexture.WhitePixel.Handle;
        }

        GLTexture.WhitePixel.Bind();
        triangleBatch.AddRange(circleDrawable.Vertices);
        triangleBatch.Draw();

        shader.SetUniform("u_IsCircle", false);

        lastBoundTextureHandle = uint.MaxValue;
    }

    private void drawMaskShape(Drawable maskDrawable, float cornerRadius)
    {
        // Bind a texture. WhitePixel is fine since we're only writing to stencil
        if (GLTexture.WhitePixel.Handle != lastBoundTextureHandle)
        {
            triangleBatch.Draw();
            GLTexture.WhitePixel.Bind();
            lastBoundTextureHandle = GLTexture.WhitePixel.Handle;
        }

        // Add the mask's vertices to the batch and draw *only* them.
        triangleBatch.AddRange(maskDrawable.Vertices);
        triangleBatch.Draw(); // Flush *just* the mask
    }

    private void drawBorder(Drawable maskDrawable, float cornerRadius, float borderThickness, Color borderColor)
    {
        if (borderThickness <= 0) return;

        triangleBatch.Draw();

        shader.SetUniform("u_IsBorder", true);
        var rect = maskDrawable.DrawRectangle;
        shader.SetUniform("u_MaskRect", new Vector4(rect.X, rect.Y, rect.Width, rect.Height));
        shader.SetUniform("u_CornerRadius", cornerRadius);
        shader.SetUniform("u_BorderThickness", borderThickness);
        shader.SetUniform("u_BorderColor", borderColor);

        // Bind white pixel for drawing the shape
        if (GLTexture.WhitePixel.Handle != lastBoundTextureHandle)
        {
            triangleBatch.Draw();
            GLTexture.WhitePixel.Bind();
            lastBoundTextureHandle = GLTexture.WhitePixel.Handle;
        }

        triangleBatch.AddRange(maskDrawable.Vertices);
        triangleBatch.Draw();

        shader.SetUniform("u_IsBorder", false);
        lastBoundTextureHandle = uint.MaxValue;
    }

    public void PushMask(Drawable maskDrawable, float cornerRadius)
    {
        triangleBatch.Draw(); // Flush all drawing before this

        // Case 1 : Scissor optimization (Rectangular)
        // For enable masking but no effect required
        if (cornerRadius <= 0.0f)
        {
            var rect = maskDrawable.DrawRectangle;

            // Calculate OpenGL Scissor Rect (Bottom-Left origin) from UI Rect (Top-Left origin)
            int scissorX = (int)(rect.X * renderScaleX);
            int scissorY = (int)(viewportHeight - (rect.Y + rect.Height) * renderScaleY);
            int scissorW = (int)(rect.Width * renderScaleX);
            int scissorH = (int)(rect.Height * renderScaleY);

            // Handle Nesting: Intersect with current scissor rect if one exists
            if (scissorStack.Count > 0)
            {
                var parentScissor = scissorStack.Peek();

                // Simple AABB intersection logic
                int newX = Math.Max(scissorX, (int)parentScissor.X);
                int newY = Math.Max(scissorY, (int)parentScissor.Y);
                int newRight = Math.Min(scissorX + scissorW, (int)(parentScissor.X + parentScissor.Z));
                int newTop = Math.Min(scissorY + scissorH, (int)(parentScissor.Y + parentScissor.W));

                scissorX = newX;
                scissorY = newY;
                scissorW = Math.Max(0, newRight - newX);
                scissorH = Math.Max(0, newTop - newY);
            }

            Vector4 finalScissor = new Vector4(scissorX, scissorY, scissorW, scissorH);
            scissorStack.Push(finalScissor);

            gl.Enable(EnableCap.ScissorTest);
            gl.Scissor(scissorX, scissorY, (uint)scissorW, (uint)scissorH);

            // increment stencilLevel to maintain logic parity, though we aren't using stencils here.
            stencilLevel++;
            return;
        }

        // Case 2 : Stencil Masking (Rounded corners or complex shapes)
        lastBoundTextureHandle = uint.MaxValue;

        gl.StencilMask(0xFF); // Enable stencil writing
        gl.ColorMask(false, false, false, false); // Disable color writing
        gl.StencilFunc(StencilFunction.Always, stencilLevel + 1, 0xFF); // Always pass test, ref is new level
        gl.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Replace); // Replace stencil value on pass

        // Draw the mask shape into the stencil buffer
        drawMaskShape(maskDrawable, cornerRadius);

        gl.StencilMask(0x00); // Disable stencil writing
        gl.ColorMask(true, true, true, true); // Re-enable color writing
        gl.StencilFunc(StencilFunction.Equal, stencilLevel + 1, 0xFF); // Only draw where stencil == new level
        gl.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Keep); // Don't change stencil buffer

        stencilLevel++;

        if (cornerRadius > 0.0f)
        {
            shader.SetUniform("u_IsMasking", true);
            var rect = maskDrawable.DrawRectangle;
            shader.SetUniform("u_MaskRect", new Vector4(rect.X, rect.Y, rect.Width, rect.Height));
            shader.SetUniform("u_CornerRadius", cornerRadius);
        }
    }

    public void PopMask(Drawable maskDrawable, float cornerRadius, float borderThickness, Color borderColor)
    {
        triangleBatch.Draw(); // Flush all drawing within the mask

        // Case 1 : Scissor optimization (Rectangular), just restore previous scissor
        if (cornerRadius <= 0.0f)
        {
            if (scissorStack.Count > 0)
                scissorStack.Pop();

            if (scissorStack.Count > 0)
            {
                // Restore parent mask
                var parentScissor = scissorStack.Peek();
                gl.Scissor((int)parentScissor.X, (int)parentScissor.Y, (uint)parentScissor.Z, (uint)parentScissor.W);
            }
            else
            {
                // No more masks
                gl.Disable(EnableCap.ScissorTest);
            }

            drawBorder(maskDrawable, cornerRadius, borderThickness, borderColor);

            stencilLevel--;
            return;
        }

        // Case 2 : Stencil Masking (Rounded corners or complex shapes)
        // Disable masking shader effects
        shader.SetUniform("u_IsMasking", false);

        gl.StencilMask(0xFF); // Enable stencil writing
        gl.ColorMask(false, false, false, false); // Disable color writing
        gl.StencilFunc(StencilFunction.Always, stencilLevel - 1, 0xFF); // Always pass, ref is parent level
        gl.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Replace); // Replace stencil value to "pop"

        // Re-draw the same shape to restore the parent's stencil level
        drawMaskShape(maskDrawable, cornerRadius);

        stencilLevel--;

        gl.StencilMask(0x00); // Disable stencil writing
        gl.ColorMask(true, true, true, true); // Re-enable color writing

        if (stencilLevel > 0)
        {
            // Still inside a parent mask
            gl.StencilFunc(StencilFunction.Equal, stencilLevel, 0xFF);
        }
        else
        {
            // No more masks, draw everywhere
            gl.StencilFunc(StencilFunction.Always, 0, 0xFF);
        }

        gl.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);

        drawBorder(maskDrawable, cornerRadius, borderThickness, borderColor);
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
