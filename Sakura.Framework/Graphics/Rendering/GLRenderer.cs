// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.Collections.Concurrent;
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
using Sakura.Framework.Statistic;
using Sakura.Framework.Timing;
using Color = Sakura.Framework.Graphics.Colors.Color;
using SakuraVertex = Sakura.Framework.Graphics.Rendering.Vertex.Vertex;
using Texture = Sakura.Framework.Graphics.Textures.Texture;

namespace Sakura.Framework.Graphics.Rendering;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class GLRenderer : IGLRenderer
{
    private static readonly GlobalStatistic<int> stat_draw_calls = GlobalStatistics.Get<int>("Renderer", "Draw Calls");
    private static readonly GlobalStatistic<int> stat_vertices_drawn = GlobalStatistics.Get<int>("Renderer", "Vertices Drawn");
    private static readonly GlobalStatistic<int> stat_shader_binds = GlobalStatistics.Get<int>("Renderer", "Shader Binds");
    private static readonly GlobalStatistic<int> stat_texture_binds = GlobalStatistics.Get<int>("Renderer", "Texture Binds");
    private static readonly GlobalStatistic<int> stat_slot_exhaustion_flushes = GlobalStatistics.Get<int>("Renderer", "Slot Exhaustion Flushes");
    private static readonly GlobalStatistic<int> stat_state_change_flushes = GlobalStatistics.Get<int>("Renderer", "State Change Flushes");
    private static readonly GlobalStatistic<int> stat_buffer_full_flushes = GlobalStatistics.Get<int>("Renderer", "Buffer Full Flushes");
    private static readonly GlobalStatistic<int> stat_drawables_updated = GlobalStatistics.Get<int>("Drawables", "Updated Last Frame");
    private static readonly GlobalStatistic<int> stat_drawables_invalidations = GlobalStatistics.Get<int>("Drawables", "Invalidations");
    private static readonly GlobalStatistic<int> stat_drawables_culled = GlobalStatistics.Get<int>("Drawables", "Culled");
    private static readonly GlobalStatistic<int> stat_drawables_drawn = GlobalStatistics.Get<int>("Drawables", "Drawn Last Frame");

    private static GL gl;

    internal static GL GL => gl;

    public Texture WhitePixel { get; private set; }
    public Matrix4x4 ProjectionMatrix => projectionMatrix;
    public Storage ShaderStorage { get; set; }

    private GLTexture whiteGLTexture => (GLTexture)WhitePixel.BackendTexture!;

    private GLShader shader;

    private Matrix4x4 projectionMatrix;

    private TriangleBatch triangleBatch;

    private int stencilLevel;

    private uint lastBoundTextureHandle = uint.MaxValue;

    private int viewportHeight;
    private readonly Stack<Vector4> scissorStack = new Stack<Vector4>();

    private readonly uint[] boundTextureHandles = new uint[8];
    private int boundTextureCount;

    private float renderScaleX = 1.0f;
    private float renderScaleY = 1.0f;

    private BlendingMode currentBlendMode = BlendingMode.Alpha;

    private readonly Stack<ClipState> clipStack = new Stack<ClipState>();
    private ClipState currentClip;

    private DrawNode rootNode;

    private readonly ConcurrentQueue<Action> drawThreadQueue = new ConcurrentQueue<Action>();

    private struct ClipState
    {
        public Vector4 ClipData;
        public float ShearX;
        public float Radius;
    }

    public unsafe void Initialize(IGraphicsSurface graphicsSurface)
    {
        var glSurface = (IOpenGLGraphicsSurface)graphicsSurface;

        gl = GL.GetApi(glSurface.GetFunctionAddress);
        if (gl == null)
            throw new InvalidOperationException("Failed to initialize OpenGL context.");

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
        resetTextureSlots();

        shader = new GLShader(gl, ShaderStorage, "shader.vert", "shader.frag");

        triangleBatch = new TriangleBatch(gl, 1000 * 12);

        lastBoundTextureHandle = uint.MaxValue;
    }

    public void SetRoot(DrawNode node)
    {
        rootNode = node;
    }

    public void Resize(int physicalWidth, int physicalHeight, int logicalWidth, int logicalHeight)
    {
        gl.Viewport(0, 0, (uint)physicalWidth, (uint)physicalHeight);
        viewportHeight = physicalHeight;
        projectionMatrix = Matrix4x4.CreateOrthographicOffCenter(0, logicalWidth, logicalHeight, 0, -1, 1);
        renderScaleX = (float)physicalWidth / logicalWidth;
        renderScaleY = (float)physicalHeight / logicalHeight;
        Logger.Debug($"Renderer resized: physical=({physicalWidth},{physicalHeight}) logical=({logicalWidth},{logicalHeight}) scale=({renderScaleX},{renderScaleY})");
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
        while (drawThreadQueue.TryDequeue(out var action))
        {
            action.Invoke();
        }
    }

    public void ScheduleToDrawThread(Action action)
    {
        drawThreadQueue.Enqueue(action);
    }

    public void FlushBatch()
    {
        triangleBatch.Draw();
        resetTextureSlots();
    }

    public void RestoreMainShader()
    {
        shader.Use();
        shader.SetUniform("u_Projection", projectionMatrix);
        int[] samplers = new int[] { 0, 1, 2, 3, 4, 5, 6, 7 };
        shader.SetUniformIntArray("u_Textures", samplers);
        shader.SetUniform("u_IsMasking", false);
        shader.SetUniform("u_IsBorder", false);
    }

    public void DisableSrgb() => gl.Disable(EnableCap.FramebufferSrgb);
    public void RestoreSrgb() => gl.Enable(EnableCap.FramebufferSrgb);

    public IShader CreateShader(Storage storage, string vertexPath, string fragmentPath) => new GLShader(gl, storage, vertexPath, fragmentPath);

    public INativeVideoTexture CreateVideoTexture(int width, int height) => new VideoGLTexture(gl, width, height);

    public void DrawVerticesRaw(ReadOnlySpan<Vertex.Vertex> vertices)
    {
        triangleBatch.DrawRaw(vertices);
    }

    public void Draw(IClock clock)
    {
        if (rootNode == null) return;

        resetTextureSlots();

        stat_draw_calls.Value = 0;
        stat_vertices_drawn.Value = 0;
        stat_shader_binds.Value = 0;
        stat_texture_binds.Value = 0;

        stat_slot_exhaustion_flushes.Value = 0;
        stat_state_change_flushes.Value = 0;
        stat_buffer_full_flushes.Value = 0;
        stat_drawables_updated.Value = 0;
        stat_drawables_invalidations.Value = 0;
        stat_drawables_culled.Value = 0;
        stat_drawables_drawn.Value = 0;

        shader.Use();
        shader.SetUniform("u_Projection", projectionMatrix);
        int[] samplers = new int[] { 0, 1, 2, 3, 4, 5, 6, 7 };
        shader.SetUniformIntArray("u_Textures", samplers);

        stencilLevel = 0;
        scissorStack.Clear();
        clipStack.Clear();
        currentClip = new ClipState
        {
            ClipData = new Vector4(0, 0, -1, -1), // -1 HalfWidth means inactive
            ShearX = 0,
            Radius = 0
        };
        shader.SetUniform("u_IsMasking", false);
        shader.SetUniform("u_IsBorder", false);

        lastBoundTextureHandle = uint.MaxValue;
        SetBlendMode(BlendingMode.Alpha);

        rootNode.Draw(this);
        triangleBatch.Draw();
    }

    public void DrawVertices(ReadOnlySpan<SakuraVertex> vertices, Texture texture)
    {
        // Video textures (VideoGLTexture) are handled by VideoDrawNode directly —
        // fall back to WhitePixel so the batch slot logic stays consistent.
        if (texture.BackendTexture == null)
            texture = WhitePixel;

        // Inside GLRenderer it's safe to cast to GLTexture for the raw uint handle.
        var glTexture = (GLTexture)texture.BackendTexture!;
        uint handle = glTexture.GLHandle;
        float textureIndex = -1;

        for (int i = 0; i < boundTextureCount; i++)
        {
            if (boundTextureHandles[i] == handle)
            {
                textureIndex = i;
                break;
            }
        }

        if (textureIndex == -1 && boundTextureCount < 8)
        {
            textureIndex = boundTextureCount;
            glTexture.Bind(boundTextureCount);
            boundTextureHandles[boundTextureCount] = handle;
            boundTextureCount++;
            stat_texture_binds.Value++;
        }

        if (textureIndex == -1)
        {
            stat_slot_exhaustion_flushes.Value++;

            triangleBatch.Draw();

            // Reset slots
            resetTextureSlots();

            // Bind to slot 0
            textureIndex = 0;
            glTexture.Bind();
            boundTextureHandles[0] = handle;
            boundTextureCount++;
            stat_texture_binds.Value++;
        }

        triangleBatch.AddRange(vertices, textureIndex, currentClip.ClipData, currentClip.ShearX, currentClip.Radius);
    }

    private void drawMaskShape(Drawable maskDrawable, float cornerRadius)
    {
        if (boundTextureCount == 0 || whiteGLTexture.GLHandle != boundTextureHandles[0])
        {
            triangleBatch.Draw();
            whiteGLTexture.Bind(0);
            boundTextureHandles[0] = whiteGLTexture.GLHandle;
            if (boundTextureCount == 0) boundTextureCount = 1;
        }

        triangleBatch.AddRange(maskDrawable.Vertices, 0f, currentClip.ClipData, currentClip.ShearX, currentClip.Radius);
        triangleBatch.Draw();
    }

    private void drawBorder(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius, float borderThickness, Color borderColor, ReadOnlySpan<SakuraVertex> vertices)
    {
        if (borderThickness <= 0 || vertices.Length == 0)
            return;

        triangleBatch.Draw();

        shader.SetUniform("u_IsBorder", true);

        shader.SetUniform("u_MaskCenter", maskCenter);
        shader.SetUniform("u_MaskHalfSize", maskHalfSize);
        shader.SetUniform("u_ShearX", shearX);
        shader.SetUniform("u_CornerRadius", cornerRadius);

        shader.SetUniform("u_BorderThickness", borderThickness);
        shader.SetUniform("u_BorderColor", borderColor);

        if (boundTextureCount == 0 || whiteGLTexture.GLHandle != boundTextureHandles[0])
        {
            triangleBatch.Draw();
            whiteGLTexture.Bind();
            boundTextureHandles[0] = whiteGLTexture.GLHandle;
            if (boundTextureCount == 0) boundTextureCount = 1;
        }

        triangleBatch.AddRange(vertices, 0f, currentClip.ClipData, currentClip.ShearX, currentClip.Radius);
        triangleBatch.Draw();

        shader.SetUniform("u_IsBorder", false);
        lastBoundTextureHandle = uint.MaxValue;
    }

    public void PushMask(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius)
    {
        clipStack.Push(currentClip);

        // calculate the true AABB of this new mask, taking horizontal shear into account
        float skewOffset = Math.Abs(shearX * maskHalfSize.Y);
        float left = maskCenter.X - maskHalfSize.X - skewOffset;
        float right = maskCenter.X + maskHalfSize.X + skewOffset;
        float top = maskCenter.Y - maskHalfSize.Y;
        float bottom = maskCenter.Y + maskHalfSize.Y;

        // if we are already inside a parent mask (Z > 0), intersect their bounding boxes
        if (currentClip.ClipData.Z > 0)
        {
            float parentSkew = Math.Abs(currentClip.ShearX * currentClip.ClipData.W);
            float pLeft = currentClip.ClipData.X - currentClip.ClipData.Z - parentSkew;
            float pRight = currentClip.ClipData.X + currentClip.ClipData.Z + parentSkew;
            float pTop = currentClip.ClipData.Y - currentClip.ClipData.W;
            float pBottom = currentClip.ClipData.Y + currentClip.ClipData.W;

            left = Math.Max(left, pLeft);
            right = Math.Min(right, pRight);
            top = Math.Max(top, pTop);
            bottom = Math.Min(bottom, pBottom);
        }

        Vector2 newCenter = new Vector2((left + right) / 2f, (top + bottom) / 2f);
        Vector2 newHalfSize = new Vector2((right - left) / 2f, (bottom - top) / 2f);

        // if the intersection collapses (the child is completely outside the parent mask),
        // we shrink the mask to practically zero so the shader correctly discards all fragments.
        if (left >= right || top >= bottom)
        {
            newHalfSize = new Vector2(0.0001f, 0.0001f);
        }
        else
        {
            // remove the skew offset again so the shader receives the true un-sheared half-size
            newHalfSize.X = Math.Max(0.0001f, newHalfSize.X - skewOffset);
        }

        currentClip = new ClipState
        {
            ClipData = new Vector4(newCenter.X, newCenter.Y, newHalfSize.X, newHalfSize.Y),
            ShearX = shearX,
            Radius = cornerRadius
        };
    }

    public void PopMask(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius, float borderThickness, Color borderColor, ReadOnlySpan<SakuraVertex> maskVertices = default)
    {
        currentClip = clipStack.Pop();
        drawBorder(maskCenter, maskHalfSize, shearX, cornerRadius, borderThickness, borderColor, maskVertices);
    }

    public void SetBlendMode(BlendingMode blendingMode)
    {
        if (blendingMode == currentBlendMode)
            return;

        stat_state_change_flushes.Value++;
        triangleBatch.Draw();

        currentBlendMode = blendingMode;

        switch (blendingMode)
        {
            case BlendingMode.Additive:
                gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
                break;

            case BlendingMode.Opaque:
                gl.BlendFunc(BlendingFactor.One, BlendingFactor.Zero);
                break;

            case BlendingMode.Multiply:
                gl.BlendFunc(BlendingFactor.DstColor, BlendingFactor.OneMinusSrcAlpha);
                break;

            case BlendingMode.Screen:
                gl.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcColor);
                break;

            case BlendingMode.Premultiplied:
                gl.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
                break;

            case BlendingMode.Alpha:
            default:
                gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                break;
        }
    }

    private void resetTextureSlots()
    {
        boundTextureCount = 0;
        Array.Clear(boundTextureHandles, 0, boundTextureHandles.Length);

        // Pre-fill all OpenGL texture units with a valid texture (WhitePixel)
        // to fix some strict driver that will complain about "unloadable texture"
        if (WhitePixel?.BackendTexture != null)
        {
            for (int i = 0; i < 8; i++)
            {
                whiteGLTexture.Bind(i);
            }
        }

        gl.ActiveTexture(TextureUnit.Texture0);
    }

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
