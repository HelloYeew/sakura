// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using Sakura.Framework.Graphics.Rendering.Batches;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.IO;
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
public class GLRenderer : IGLRenderer, IDisposable
{
    private static readonly GlobalStatistic<int> stat_draw_calls = GlobalStatistics.Get<int>("Renderer", "Draw Calls");
    private static readonly GlobalStatistic<int> stat_vertices_drawn = GlobalStatistics.Get<int>("Renderer", "Vertices Drawn");
    private static readonly GlobalStatistic<int> stat_shader_binds = GlobalStatistics.Get<int>("Renderer", "Shader Binds");
    private static readonly GlobalStatistic<int> stat_texture_binds = GlobalStatistics.Get<int>("Renderer", "Texture Binds");

    private static readonly GlobalStatistic<int> stat_texture_binds_last_frame = GlobalStatistics.Get<int>("Renderer", "Texture Binds (Last Frame)");
    private static readonly GlobalStatistic<int> stat_slot_exhaustion_flushes = GlobalStatistics.Get<int>("Renderer", "Slot Exhaustion Flushes");
    private static readonly GlobalStatistic<int> stat_state_change_flushes = GlobalStatistics.Get<int>("Renderer", "State Change Flushes");
    private static readonly GlobalStatistic<int> stat_buffer_full_flushes = GlobalStatistics.Get<int>("Renderer", "Buffer Full Flushes");
    private static readonly GlobalStatistic<int> stat_drawables_updated = GlobalStatistics.Get<int>("Drawables", "Updated Last Frame");
    private static readonly GlobalStatistic<int> stat_drawables_invalidations = GlobalStatistics.Get<int>("Drawables", "Invalidations");
    private static readonly GlobalStatistic<int> stat_drawables_culled = GlobalStatistics.Get<int>("Drawables", "Culled");
    private static readonly GlobalStatistic<int> stat_drawables_drawn = GlobalStatistics.Get<int>("Drawables", "Drawn Last Frame");

    private static GL gl;

    internal static GL GL => gl;

    // The live renderer instance, for static notifications (e.g. texture deletion).
    // Effectively a singleton: only one GL renderer exists per process.
    private static GLRenderer instance;

    /// <summary>
    /// Must be called whenever a GL texture is deleted. GL recycles deleted handle IDs,
    /// so a new texture (e.g. a glyph atlas page or a recreated framebuffer attachment)
    /// can receive the same numeric handle as a deleted one — if the CPU-side slot
    /// tracking still maps that handle to a slot, the renderer would skip binding and
    /// draw with whatever texture happens to occupy the slot.
    /// </summary>
    internal static void NotifyTextureDeleted(uint handle)
    {
        var renderer = instance;
        if (renderer == null || handle == 0)
            return;

        for (int i = 0; i < renderer.boundTextureHandles.Length; i++)
        {
            if (renderer.boundTextureHandles[i] == handle)
                renderer.boundTextureHandles[i] = uint.MaxValue; // never matches a live handle
        }

        if (renderer.lastBoundTextureHandle == handle)
            renderer.lastBoundTextureHandle = uint.MaxValue;
    }

    public Texture WhitePixel { get; private set; }
    public Matrix4x4 ProjectionMatrix => projectionMatrix;
    public Storage ShaderStorage { get; set; }
    public DiskCache ShaderCache { get; set; }

    private GLTexture whiteGLTexture => (GLTexture)WhitePixel.BackendTexture!;

    private GLShader shader;

    private Uniforms.GLUniformBuffer<Uniforms.ProjectionBlock> projectionBuffer;
    private Uniforms.GLUniformBuffer<Uniforms.MaskBlock> maskBuffer;
    private Uniforms.MaskBlock maskState;

    private const uint projection_binding = 0;
    private const uint mask_binding = 1;

    private void uploadProjection()
    {
        projectionBuffer.Update(new Uniforms.ProjectionBlock { Projection = projectionMatrix });
    }

    private void uploadMaskState() => maskBuffer.Update(maskState);

    private Matrix4x4 projectionMatrix;

    private TriangleBatch triangleBatch;

    private int stencilLevel;

    private uint lastBoundTextureHandle = uint.MaxValue;

    private int viewportHeight;
    private readonly Stack<Vector4> scissorStack = new Stack<Vector4>();

    /// <summary>
    /// Maximum number of textures the shader can sample in one draw. Matches the size of
    /// <c>u_Textures[]</c> in shader.frag. The effective count used at runtime is clamped to
    /// <c>GL_MAX_TEXTURE_IMAGE_UNITS</c> in <see cref="textureSlotCount"/>.
    /// </summary>
    private const int max_texture_slots = 16;

    private readonly uint[] boundTextureHandles = new uint[max_texture_slots];
    private int boundTextureCount;

    /// <summary>
    /// Effective number of batch texture slots: <c>min(<see cref="max_texture_slots"/>,
    /// GL_MAX_TEXTURE_IMAGE_UNITS)</c>. Set at initialization so the renderer never assigns a slot
    /// index the driver cannot sample. Drives the slot-exhaustion check and the prefill loop.
    /// </summary>
    private int textureSlotCount = max_texture_slots;

    private float renderScaleX = 1.0f;
    private float renderScaleY = 1.0f;

    private BlendingMode currentBlendMode = BlendingMode.Alpha;

    private readonly Stack<ClipState> clipStack = new Stack<ClipState>();
    private ClipState currentClip;

    private DrawNode rootNode;

    private readonly ConcurrentQueue<Action> drawThreadQueue = new ConcurrentQueue<Action>();

    private uint currentFrameBufferHandle;
    private int currentViewportWidth = 1;
    private int currentViewportHeight = 1;
    private int windowViewportWidth = 1;
    private int windowViewportHeight = 1;
    private readonly Stack<FrameBufferState> frameBufferStack = new Stack<FrameBufferState>();

    private struct FrameBufferState
    {
        public uint Handle;
        public int ViewportWidth;
        public int ViewportHeight;
        public Matrix4x4 Projection;
        public ClipState Clip;
    }

    /// <summary>
    /// Sampler-to-texture-unit mapping for <c>u_Textures[]</c>, sized to <see cref="textureSlotCount"/>
    /// and populated with <c>0..textureSlotCount-1</c> at initialization.
    /// </summary>
    private int[] textureSamplers = { 0, 1, 2, 3, 4, 5, 6, 7 };

    private struct ClipState
    {
        public Vector4 ClipData;
        public float ShearX;
        public float Radius;
    }

    public unsafe void Initialize(IGraphicsSurface graphicsSurface)
    {
        instance = this;

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
        string extensions = GetExtensions();
        Logger.Verbose($"GL Extensions: {extensions}");

        Logger.Verbose("🚅 Hardware Acceleration Information");
        Logger.Verbose($"JIT intrinsic support: {RuntimeInfo.IsIntrinsicSupported}");

        // Clamp the batch texture-slot count to what the driver can actually sample. The shader's
        // u_Textures[] array is fixed at max_texture_slots (16, the GL spec minimum for
        // GL_MAX_TEXTURE_IMAGE_UNITS), but a driver may expose exactly 16 or — defensively — fewer;
        // never hand the shader a slot index the hardware can't sample.
        gl.GetInteger(GetPName.MaxTextureImageUnits, out int maxTextureImageUnits);
        textureSlotCount = Math.Clamp(maxTextureImageUnits, 1, max_texture_slots);
        Logger.Verbose($"GL Max Texture Image Units: {maxTextureImageUnits} (using {textureSlotCount} batch slots)");

        textureSamplers = new int[textureSlotCount];
        for (int i = 0; i < textureSlotCount; i++)
            textureSamplers[i] = i;

        tryEnableDebugOutput(extensions);

        gl.ClearColor(Color.Black);

        gl.Enable(EnableCap.Blend);
        applyBlend(BlendingMode.Alpha);

        gl.Enable(EnableCap.FramebufferSrgb);

        gl.Enable(EnableCap.StencilTest);
        gl.StencilMask(0xFF);
        gl.ClearStencil(0);
        gl.Clear(ClearBufferMask.StencilBufferBit);
        gl.StencilMask(0x00);

        gl.Disable(EnableCap.ScissorTest);

        GLTexture.CreateWhitePixel(gl);
        WhitePixel = new Texture(GLTexture.WhitePixel);
        prefillTextureSlots();
        resetTextureSlots();

        var (mainVert, mainFrag) = ShaderCompiler.GetOrCompile(
            ShaderStorage, "shader.vert", "shader.frag", SPIRV.CrossCompileTarget.GLSL, ShaderCache);
        shader = new GLShader(gl, mainVert, mainFrag);

        projectionBuffer = new Uniforms.GLUniformBuffer<Uniforms.ProjectionBlock>(gl, projection_binding);
        maskBuffer = new Uniforms.GLUniformBuffer<Uniforms.MaskBlock>(gl, mask_binding);

        if (!shader.BindUniformBlock("ProjectionBlock", projection_binding))
            Logger.Error("GLRenderer: main shader is missing expected uniform block 'ProjectionBlock'.");
        if (!shader.BindUniformBlock("MaskBlock", mask_binding))
            Logger.Error("GLRenderer: main shader is missing expected uniform block 'MaskBlock'.");

        shader.Use();
        shader.SetUniformIntArray("u_Textures", textureSamplers);

        maskState = default;

        triangleBatch = new TriangleBatch(gl, 1000 * 12);

        lastBoundTextureHandle = uint.MaxValue;
    }

    public void SetRoot(DrawNode rootDrawNode)
    {
        rootNode = rootDrawNode;
    }

    public void Resize(int physicalWidth, int physicalHeight, int logicalWidth, int logicalHeight)
    {
        gl.Viewport(0, 0, (uint)physicalWidth, (uint)physicalHeight);
        viewportHeight = physicalHeight;
        windowViewportWidth = physicalWidth;
        windowViewportHeight = physicalHeight;
        currentViewportWidth = physicalWidth;
        currentViewportHeight = physicalHeight;
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

        uploadProjection();
        projectionBuffer.Bind();

        maskState.IsMasking = 0;
        maskState.IsBorder = 0;
        uploadMaskState();
        maskBuffer.Bind();
    }

    public void DisableSrgb() => gl.Disable(EnableCap.FramebufferSrgb);
    public void RestoreSrgb() => gl.Enable(EnableCap.FramebufferSrgb);

    public IShader CreateShader(Storage storage, string vertexPath, string fragmentPath)
    {
        (string vert, string frag) = ShaderCompiler.GetOrCompile(
            storage, vertexPath, fragmentPath, SPIRV.CrossCompileTarget.GLSL, ShaderCache);
        return new GLShader(gl, vert, frag);
    }

    public INativeVideoTexture CreateVideoTexture(int width, int height) => new VideoGLTexture(gl, width, height);

    public INativeTexture CreateNativeTexture(int width, int height) => new GLTexture(gl, width, height);

    public void DrawVerticesRaw(ReadOnlySpan<Vertex.Vertex> vertices)
    {
        if (vertices.Length == 4)
        {
            Span<SakuraVertex> expanded = stackalloc SakuraVertex[6];
            expanded[0] = vertices[0];
            expanded[1] = vertices[1];
            expanded[2] = vertices[2];
            expanded[3] = vertices[2];
            expanded[4] = vertices[3];
            expanded[5] = vertices[0];
            triangleBatch.DrawRaw(expanded);
            return;
        }

        triangleBatch.DrawRaw(vertices);
    }

    public void Draw(IClock clock)
    {
        if (rootNode == null) return;

        throttleFramesInFlight();

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

        uploadProjection();
        projectionBuffer.Bind();

        stencilLevel = 0;
        scissorStack.Clear();
        clipStack.Clear();
        currentClip = new ClipState
        {
            ClipData = new Vector4(0, 0, -1, -1), // -1 HalfWidth means inactive
            ShearX = 0,
            Radius = 0
        };
        maskState.IsMasking = 0;
        maskState.IsBorder = 0;
        uploadMaskState();
        maskBuffer.Bind();

        lastBoundTextureHandle = uint.MaxValue;
        SetBlendMode(BlendingMode.Alpha);

        // Defensive: if a previous frame aborted mid-offscreen-pass, don't leak its state.
        if (frameBufferStack.Count > 0)
        {
            frameBufferStack.Clear();
            currentFrameBufferHandle = 0;
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            gl.Viewport(0, 0, (uint)windowViewportWidth, (uint)windowViewportHeight);
            currentViewportWidth = windowViewportWidth;
            currentViewportHeight = windowViewportHeight;
        }

        rootNode.Draw(this);
        triangleBatch.Draw();

        // Publish a stable snapshot of this frame's totals for cross-thread consumers (e.g. the
        // texture viewer). Done after the final batch flush so all binds for the frame are counted.
        stat_texture_binds_last_frame.Value = stat_texture_binds.Value;

        insertFrameFence();
    }

    private const int max_frames_in_flight = 1;
    private readonly Queue<nint> frameFences = new Queue<nint>();

    /// <summary>
    /// Blocks until the oldest outstanding frame fence signals whenever the in-flight limit is
    /// reached, so the draw thread never runs more than <see cref="max_frames_in_flight"/> frames
    /// ahead of the GPU. Sync objects require GL 3.2+ / ARB_sync (always present in 4.x context).
    /// </summary>
    private void throttleFramesInFlight()
    {
        while (frameFences.Count >= max_frames_in_flight)
        {
            nint fence = frameFences.Dequeue();
            if (fence == nint.Zero)
                continue;

            // SyncFlushCommandsBit guarantees the fence is reached by the GPU. Wait over a finite
            // timeout, retrying a few times so a never-signalled fence can't hang the draw thread.
            GLEnum result;
            int guard = 0;
            do
            {
                result = gl.ClientWaitSync(fence, (uint)GLEnum.SyncFlushCommandsBit, 1_000_000_000UL);
            }
            while (result == GLEnum.TimeoutExpired && ++guard < 3);

            gl.DeleteSync(fence);
        }
    }

    private void insertFrameFence()
    {
        nint fence = gl.FenceSync(SyncCondition.SyncGpuCommandsComplete, 0u);
        if (fence != nint.Zero)
            frameFences.Enqueue(fence);
    }

    public Vector2 RenderScale => new Vector2(renderScaleX, renderScaleY);

    /// <summary>
    /// Creates an offscreen render target. Must be called on the draw thread.
    /// </summary>
    public IFrameBuffer CreateFrameBuffer(int width, int height, bool pixelSnapping = false) => new GLFrameBuffer(gl, width, height, pixelSnapping);

    public void BindFrameBuffer(IFrameBuffer frameBuffer, RectangleF sourceRect, Color clearColour = default)
    {
        var glFrameBuffer = (GLFrameBuffer)frameBuffer;

        // Anything batched so far targets the previous render target — flush it there first.
        triangleBatch.Draw();
        resetTextureSlots();

        frameBufferStack.Push(new FrameBufferState
        {
            Handle = currentFrameBufferHandle,
            ViewportWidth = currentViewportWidth,
            ViewportHeight = currentViewportHeight,
            Projection = projectionMatrix,
            Clip = currentClip
        });

        currentFrameBufferHandle = glFrameBuffer.Handle;
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, glFrameBuffer.Handle);
        gl.Viewport(0, 0, (uint)glFrameBuffer.Width, (uint)glFrameBuffer.Height);
        currentViewportWidth = glFrameBuffer.Width;
        currentViewportHeight = glFrameBuffer.Height;

        // Map the captured logical screen-space rect onto the buffer using the same
        // top-left-origin convention as the main projection, so draw nodes render with
        // their unchanged screen-space coordinates.
        projectionMatrix = Matrix4x4.CreateOrthographicOffCenter(
            sourceRect.X, sourceRect.X + sourceRect.Width,
            sourceRect.Y + sourceRect.Height, sourceRect.Y,
            -1, 1);
        uploadProjection();

        // Content inside the buffer starts from a clean clip state; the outer clip applies
        // to the final composited quad instead.
        currentClip = new ClipState
        {
            ClipData = new Vector4(0, 0, -1, -1),
            ShearX = 0,
            Radius = 0
        };

        gl.ClearColor(clearColour.R / 255f, clearColour.G / 255f, clearColour.B / 255f, clearColour.A / 255f);
        gl.Clear(ClearBufferMask.ColorBufferBit);
        gl.ClearColor(Color.Black);
    }

    public void UnbindFrameBuffer()
    {
        if (frameBufferStack.Count == 0)
            throw new InvalidOperationException($"{nameof(UnbindFrameBuffer)} was called without a matching {nameof(BindFrameBuffer)}.");

        // Flush geometry rendered into the framebuffer before switching away from it.
        triangleBatch.Draw();
        resetTextureSlots();

        var state = frameBufferStack.Pop();

        currentFrameBufferHandle = state.Handle;
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, state.Handle);
        gl.Viewport(0, 0, (uint)state.ViewportWidth, (uint)state.ViewportHeight);
        currentViewportWidth = state.ViewportWidth;
        currentViewportHeight = state.ViewportHeight;

        projectionMatrix = state.Projection;
        uploadProjection();

        currentClip = state.Clip;
    }

    /// <summary>
    /// Resolves a texture to a batch slot index, binding it (and flushing on slot
    /// exhaustion) as required.
    /// </summary>
    private float prepareTexture(Texture texture)
    {
        // Inside GLRenderer it's safe to cast to GLTexture for the raw uint handle.
        var glTexture = (GLTexture)texture.BackendTexture!;
        uint handle = glTexture.GLHandle;

        for (int i = 0; i < boundTextureCount; i++)
        {
            if (boundTextureHandles[i] == handle)
                return i;
        }

        if (boundTextureCount < textureSlotCount)
        {
            int slot = boundTextureCount;
            glTexture.Bind(slot);
            boundTextureHandles[slot] = handle;
            boundTextureCount++;
            stat_texture_binds.Value++;
            return slot;
        }

        // All slots taken, flush and start a fresh slot set.
        stat_slot_exhaustion_flushes.Value++;
        triangleBatch.Draw();
        resetTextureSlots();

        glTexture.Bind(0);
        boundTextureHandles[0] = handle;
        boundTextureCount = 1;
        stat_texture_binds.Value++;
        return 0;
    }

    public void DrawVertices(ReadOnlySpan<SakuraVertex> vertices, Texture texture)
    {
        // Video textures (VideoGLTexture) are handled by VideoDrawNode directly —
        // fall back to WhitePixel so the batch slot logic stays consistent.
        if (texture.BackendTexture == null)
            texture = WhitePixel;

        float textureIndex = prepareTexture(texture);

        triangleBatch.AddRange(vertices, textureIndex, currentClip.ClipData, currentClip.ShearX, currentClip.Radius);
    }

    public void DrawQuads(ReadOnlySpan<SakuraVertex> vertices, Texture texture)
    {
        if (texture.BackendTexture == null)
            texture = WhitePixel;

        float textureIndex = prepareTexture(texture);

        for (int i = 0; i + 4 <= vertices.Length; i += 4)
            triangleBatch.AddQuad(vertices.Slice(i, 4), textureIndex, currentClip.ClipData, currentClip.ShearX, currentClip.Radius);
    }

    private void drawBorder(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius, float borderThickness, Color borderColor, ReadOnlySpan<SakuraVertex> vertices)
    {
        if (borderThickness <= 0 || vertices.Length == 0)
            return;

        triangleBatch.Draw();

        maskState.IsBorder = 1;
        maskState.MaskCenter = new System.Numerics.Vector2(maskCenter.X, maskCenter.Y);
        maskState.MaskHalfSize = new System.Numerics.Vector2(maskHalfSize.X, maskHalfSize.Y);
        maskState.ShearX = shearX;
        maskState.CornerRadius = cornerRadius;
        maskState.BorderThickness = borderThickness;
        maskState.BorderColor = new System.Numerics.Vector4(
            borderColor.R / 255f, borderColor.G / 255f, borderColor.B / 255f, borderColor.A / 255f);
        uploadMaskState();

        if (boundTextureCount == 0 || whiteGLTexture.GLHandle != boundTextureHandles[0])
        {
            triangleBatch.Draw();
            whiteGLTexture.Bind();
            boundTextureHandles[0] = whiteGLTexture.GLHandle;
            if (boundTextureCount == 0) boundTextureCount = 1;
        }

        // The mask geometry is a single quad (TL, TR, BR, BL).
        if (vertices.Length >= 4)
            triangleBatch.AddQuad(vertices.Slice(0, 4), 0f, currentClip.ClipData, currentClip.ShearX, currentClip.Radius);

        triangleBatch.Draw();

        maskState.IsBorder = 0;
        uploadMaskState();
        lastBoundTextureHandle = uint.MaxValue;
    }

    public void DrawEdgeEffect(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius, float edgeRadius, Vector2 offset, Color color, bool glow, bool hollow, ReadOnlySpan<SakuraVertex> quadVertices)
    {
        if (color.A == 0 || quadVertices.Length < 4)
            return;

        triangleBatch.Draw();

        var previousBlend = currentBlendMode;
        if (glow)
            SetBlendMode(BlendingMode.Additive);

        maskState.IsEdgeEffect = 1;
        maskState.MaskCenter = new System.Numerics.Vector2(maskCenter.X, maskCenter.Y);
        maskState.MaskHalfSize = new System.Numerics.Vector2(maskHalfSize.X, maskHalfSize.Y);
        maskState.ShearX = shearX;
        maskState.CornerRadius = cornerRadius;
        maskState.EdgeRadius = edgeRadius;
        maskState.EdgeOffset = new System.Numerics.Vector2(offset.X, offset.Y);
        maskState.EdgeHollow = hollow ? 1 : 0;
        maskState.EdgeGlow = glow ? 1 : 0;
        maskState.BorderColor = new System.Numerics.Vector4(
            color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
        uploadMaskState();

        if (boundTextureCount == 0 || whiteGLTexture.GLHandle != boundTextureHandles[0])
        {
            triangleBatch.Draw();
            whiteGLTexture.Bind();
            boundTextureHandles[0] = whiteGLTexture.GLHandle;
            if (boundTextureCount == 0) boundTextureCount = 1;
        }

        triangleBatch.AddQuad(quadVertices.Slice(0, 4), 0f, currentClip.ClipData, currentClip.ShearX, currentClip.Radius);
        triangleBatch.Draw();

        maskState.IsEdgeEffect = 0;
        uploadMaskState();
        lastBoundTextureHandle = uint.MaxValue;

        if (glow)
            SetBlendMode(previousBlend);
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
        applyBlend(blendingMode);
    }

    /// <summary>
    /// Issues the GL blend state for a mode. The alpha channel is blended separately from RGB so
    /// coverage accumulates correctly when rendering into a (possibly transparent) offscreen target:
    /// a single <c>glBlendFunc</c> would apply the RGB source factor to alpha too, producing wrong
    /// composited alpha for buffered containers and edge effects.
    /// </summary>
    private void applyBlend(BlendingMode blendingMode)
    {
        switch (blendingMode)
        {
            case BlendingMode.Additive:
                gl.BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.One, BlendingFactor.One, BlendingFactor.One);
                break;

            case BlendingMode.Opaque:
                gl.BlendFuncSeparate(BlendingFactor.One, BlendingFactor.Zero, BlendingFactor.One, BlendingFactor.Zero);
                break;

            case BlendingMode.Multiply:
                gl.BlendFuncSeparate(BlendingFactor.DstColor, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
                break;

            case BlendingMode.Screen:
                gl.BlendFuncSeparate(BlendingFactor.One, BlendingFactor.OneMinusSrcColor, BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
                break;

            case BlendingMode.Premultiplied:
                gl.BlendFuncSeparate(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
                break;

            case BlendingMode.Alpha:
            default:
                gl.BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
                break;
        }
    }

    private void resetTextureSlots()
    {
        // Only the CPU-side slot tracking needs resetting: textures stay bound on the GPU
        // and will simply be re-tracked (or replaced) on their next use. All units are
        // pre-filled with the white pixel once at initialization for strict drivers.
        boundTextureCount = 0;
        Array.Clear(boundTextureHandles, 0, boundTextureHandles.Length);
    }

    private void prefillTextureSlots()
    {
        // Pre-fill all OpenGL texture units with a valid texture (WhitePixel)
        // to fix some strict drivers that complain about "unloadable texture".
        if (WhitePixel?.BackendTexture != null)
        {
            for (int i = 0; i < textureSlotCount; i++)
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

    private DebugProc debugMessageCallback;

    /// <summary>
    /// Wires <c>glDebugMessageCallback</c> (KHR_debug / GL 4.3+) so driver errors surface in the log
    /// instead of failing silently. Skipped when the driver doesn't expose the extension.
    /// </summary>
    private unsafe void tryEnableDebugOutput(string extensions)
    {
        if (!extensions.Contains("GL_KHR_debug"))
            return;

        try
        {
            debugMessageCallback = onDebugMessage;
            gl.Enable(EnableCap.DebugOutput);
            gl.DebugMessageCallback(debugMessageCallback, null);
            Logger.Verbose("GL debug output enabled (KHR_debug).");
        }
        catch (Exception ex)
        {
            debugMessageCallback = null;
            Logger.Verbose($"GL debug output unavailable: {ex.Message}");
        }
    }

    private static void onDebugMessage(GLEnum source, GLEnum type, int id, GLEnum severity, int length, nint message, nint userParam)
    {
        string text = Marshal.PtrToStringAnsi(message, length);

        switch (severity)
        {
            case GLEnum.DebugSeverityHigh:
                Logger.Error($"[GL] {text}");
                break;

            case GLEnum.DebugSeverityMedium:
                Logger.Warning($"[GL] {text}");
                break;

            default:
                Logger.Verbose($"[GL] {text}");
                break;
        }
    }

    private bool disposed;

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        while (frameFences.Count > 0)
        {
            nint fence = frameFences.Dequeue();
            if (fence != nint.Zero)
                gl.DeleteSync(fence);
        }

        shader?.Dispose();
        triangleBatch?.Dispose();
        projectionBuffer?.Dispose();
        maskBuffer?.Dispose();
        (WhitePixel?.BackendTexture as GLTexture)?.Dispose();

        if (instance == this)
            instance = null;

        GC.SuppressFinalize(this);
    }
}
