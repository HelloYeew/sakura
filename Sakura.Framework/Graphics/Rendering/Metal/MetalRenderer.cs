// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Rendering.Uniforms;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.IO;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Timing;
using SakuraVertex = Sakura.Framework.Graphics.Rendering.Vertex.Vertex;

namespace Sakura.Framework.Graphics.Rendering.Metal;

/// <summary>
/// Metal renderer backend
/// </summary>
public sealed class MetalRenderer : IMetalRenderer
{
    private nint device; // SakuraMetalDevice*
    private MetalShader mainShader;

    private MetalShader currentShader;
    private BlendingMode currentBlendMode = BlendingMode.Alpha;

    private readonly ConcurrentQueue<Action> drawThreadQueue = new();

    private DrawNode rootNode;
    private Matrix4x4 projectionMatrix = Matrix4x4.Identity;

    /// <summary>
    /// Projection UBO buffer index on the vertex stage. From the cross-compiled MSL the ProjectionBlock
    /// is `constant _17& _19 [[buffer(0)]]`, so the projection goes to vertex buffer 0.
    /// </summary>
    private const int projection_buffer_index = 0;

    /// <summary>
    /// MaskBlock UBO buffer index on the fragment stage. The sakura-spirv bridge assigns MSL buffer
    /// indices by iterating resources sorted by (set, binding) with an independent per-stage buffer
    /// counter (see CompileVertexFragment / GetResourceIndex in libsakura-spirv.cpp): ProjectionBlock
    /// (set 0, binding 0, vertex) -> vertex buffer 0; MaskBlock (set 0, binding 1, fragment) -> fragment
    /// buffer 1; u_Textures[] (set 1, binding 0) -> texture/sampler 0. So MaskBlock is fragment buffer 1.
    /// </summary>
    private const int mask_buffer_index = 1;

    private static readonly (float r, float g, float b, float a) clear_color = (0f, 0f, 0f, 1f);

    private float renderScaleX = 1.0f;
    private float renderScaleY = 1.0f;

    /// <summary>
    ///  Masking/clip state, mirroring GLRenderer. Clip data is carried per-vertex(the fragment shader's
    /// applyClipping reads v_ClipData/v_ClipShearX/v_ClipRadius), so DrawVertices/DrawQuads inject the
    /// current clip into every vertex — exactly what GL's TriangleBatch does in AddRange/AddQuad. The
    /// MaskBlock UBO is used only for the border pass in PopMask (u_IsBorder path in shader.frag).
    /// </summary>
    private readonly Stack<ClipState> clipStack = new Stack<ClipState>();
    private ClipState currentClip;
    private MaskBlock maskState;

    private struct ClipState
    {
        public Vector4 ClipData;
        public float ShearX;
        public float Radius;
    }

    /// <summary>
    /// Offscreen render-target stack, mirroring GLRenderer's frameBufferStack. On BindFrameBuffer the
    /// current projection/clip are saved and a new offscreen pass is opened natively; UnbindFrameBuffer
    /// pops both back. Each pass switch reopens a fresh native encoder (which loses all bindings), so we
    /// re-bind the pipeline + projection + mask after every switch.
    /// </summary>
    private readonly Stack<FrameBufferState> frameBufferStack = new Stack<FrameBufferState>();

    private struct FrameBufferState
    {
        public Matrix4x4 Projection;
        public ClipState Clip;
    }

    public Texture WhitePixel { get; private set; }
    public Matrix4x4 ProjectionMatrix => projectionMatrix;
    public Storage ShaderStorage { get; set; }
    public DiskCache ShaderCache { get; set; }

    public Vector2 RenderScale => new Vector2(renderScaleX, renderScaleY);

    public void Initialize(IGraphicsSurface graphicsSurface)
    {
        if (graphicsSurface is not IMetalGraphicsSurface metalSurface)
            throw new InvalidOperationException($"{nameof(MetalRenderer)} requires an {nameof(IMetalGraphicsSurface)}.");

        SakuraMetalNative.SetupLibraryResolvers();

        device = SakuraMetalNative.sakura_metal_create(metalSurface.MetalLayer);
        if (device == nint.Zero)
            throw new InvalidOperationException("Failed to create Metal device from the CAMetalLayer.");

        // Cross-compile the main shader to MSL (cached) and build the pipeline with a vertex layout
        // matching the Vertex struct (attribute index = GLSL location, offset = struct field offset).
        var (vertMsl, fragMsl) = ShaderCompiler.GetOrCompile(
            ShaderStorage, "shader.vert", "shader.frag", SPIRV.CrossCompileTarget.MSL, ShaderCache);

        var attributes = buildVertexAttributes();
        mainShader = new MetalShader(device, vertMsl, fragMsl, attributes, SakuraVertex.Size, mainShaderUniformBindings());
        currentShader = mainShader;

        // 1x1 white pixel. The main shader always samples u_Textures[index]; solid-color drawables
        // sample this white texel so the result is white × v_Color. Bound to slot 0 each frame.
        var whiteTex = new MetalTexture(device, 1, 1);
        whiteTex.Upload(new byte[] { 255, 255, 255, 255 });
        MetalTexture.WhitePixel = whiteTex; // shared fallback for un-uploaded textures (see MetalTexture.Bind)
        WhitePixel = new Texture(whiteTex);

        logDeviceInfo();
    }

    /// <summary>
    /// Logs Metal device and capability info at startup
    /// </summary>
    private unsafe void logDeviceInfo()
    {
        Logger.Verbose("🤘 Metal renderer initialized");

        var info = new MetalDeviceInfo();
        const int name_capacity = 256;
        byte* nameBuffer = stackalloc byte[name_capacity];

        SakuraMetalNative.sakura_metal_get_info(device, &info, nameBuffer, name_capacity);

        string deviceName = Encoding.UTF8.GetString(nameBuffer, lengthOf(nameBuffer, name_capacity));

        // The "family" is Metal's analogue of GL's version/feature tier (e.g. Apple7, Mac2).
        string family = info.SupportsFamilyApple > 0 ? $"Apple{info.SupportsFamilyApple}"
            : info.SupportsFamilyMac > 0 ? $"Mac{info.SupportsFamilyMac}"
            : "unknown";

        Logger.Verbose($"Metal Device: {deviceName}");
        Logger.Verbose($"Metal GPU Family: {family}");
        Logger.Verbose($"Metal Unified Memory: {info.HasUnifiedMemory != 0}");
        Logger.Verbose($"Metal Max Threads/Threadgroup: {info.MaxThreadsPerThreadgroup}");
        if (info.RecommendedMaxWorkingSetSize > 0)
            Logger.Verbose($"Metal Recommended Working Set: {info.RecommendedMaxWorkingSetSize / (1024 * 1024)} MB");

        Logger.Verbose("🚅 Hardware Acceleration Information");
        Logger.Verbose($"JIT intrinsic support: {RuntimeInfo.IsIntrinsicSupported}");
    }

    /// <summary>
    /// Length of a null-terminated byte buffer, capped at max.
    /// </summary>
    private static unsafe int lengthOf(byte* buffer, int max)
    {
        int len = 0;
        while (len < max && buffer[len] != 0)
            len++;
        return len;
    }

    /// <summary>
    /// Maps each Vertex field to a shader attribute location, matching TriangleBatch's GL layout:
    /// 0=Position, 1=TexCoords, 2=Color, 3=TexIndex, 4=ClipData, 5=ClipShearX, 6=ClipRadius.
    /// </summary>
    private static MetalVertexAttribute[] buildVertexAttributes() =>
    [
        attr(0, 2, nameof(SakuraVertex.Position)),
        attr(1, 2, nameof(SakuraVertex.TexCoords)),
        attr(2, 4, nameof(SakuraVertex.Color)),
        attr(3, 1, nameof(SakuraVertex.TexIndex)),
        attr(4, 4, nameof(SakuraVertex.ClipData)),
        attr(5, 1, nameof(SakuraVertex.ClipShearX)),
        attr(6, 1, nameof(SakuraVertex.ClipRadius)),
    ];

    private static MetalVertexAttribute attr(int index, int components, string field) =>
        new MetalVertexAttribute
        {
            AttributeIndex = index,
            ComponentCount = components,
            Offset = (int)Marshal.OffsetOf<SakuraVertex>(field),
        };

    /// <summary>
    /// MSL uniform-block buffer indices are deterministic from the sakura-spirv resource ordering:
    /// resources are sorted by (set, binding) and assigned independent per-stage counters (UBOs →
    /// bufferIndex++, sampled images → textureIndex++). The vertex stage and fragment stage each have
    /// their own buffer namespace in Metal, so ProjectionBlock (vertex) and a fragment UBO can both be
    /// buffer index 0/1 without colliding. See GetResourceIndex/CompileVertexFragment in the bridge.

    /// Main shader (shader.vert + shader.frag): ProjectionBlock (set 0,b0,vertex)→vertex buffer 0;
    /// MaskBlock (set 0,b1,fragment)→fragment buffer 1.
    /// </summary>
    /// <returns></returns>
    private static IReadOnlyDictionary<string, MetalShader.UniformBinding> mainShaderUniformBindings() =>
        new Dictionary<string, MetalShader.UniformBinding>
        {
            ["ProjectionBlock"] = new MetalShader.UniformBinding(MetalShader.Stage.Vertex, projection_buffer_index),
            ["MaskBlock"] = new MetalShader.UniformBinding(MetalShader.Stage.Fragment, mask_buffer_index),
        };

    /// <summary>
    /// Custom shaders for BufferedContainer effects (blur/grayscale) and video. The bridge walks
    /// resources sorted by (set, binding) with one global buffer counter, decorating each resource on
    /// its own stage. For all three effect/video fragment shaders the shape is identical:
    ///   (0,0) ProjectionBlock [vertex UBO]                        → vertex buffer 0
    ///   (0,2|0,3|0,4) Grayscale/Blur/VideoBlock [single frag UBO] → fragment buffer 1
    ///   (1,0..) u_Texture / u_TextureY,U,V [sampled images]       → texture/sampler 0 (and 1,2 for video)
    /// Metal buffer indices are per-stage, so the fragment UBO at [[buffer(1)]] (with no fragment
    /// buffer 0) is correct — same shape as the main shader's MaskBlock. Each shader only looks up the
    /// block names it actually uses, so one combined map serves all custom shaders.
    /// </summary>
    private static IReadOnlyDictionary<string, MetalShader.UniformBinding> customShaderUniformBindings() =>
        new Dictionary<string, MetalShader.UniformBinding>
        {
            ["ProjectionBlock"] = new MetalShader.UniformBinding(MetalShader.Stage.Vertex, 0),
            ["BlurBlock"] = new MetalShader.UniformBinding(MetalShader.Stage.Fragment, 1),
            ["GrayscaleBlock"] = new MetalShader.UniformBinding(MetalShader.Stage.Fragment, 1),
            ["VideoBlock"] = new MetalShader.UniformBinding(MetalShader.Stage.Fragment, 1),
        };

    public void Clear()
    {
        // Folded into begin_frame (load action = clear).
    }

    public void StartFrame()
    {
        if (device == nint.Zero)
            return;

        // Drain queued uploads (textures, glyphs) on the draw thread, before the render pass opens.
        while (drawThreadQueue.TryDequeue(out var action))
            action();

        SakuraMetalNative.sakura_metal_begin_frame(device, clear_color.r, clear_color.g, clear_color.b, clear_color.a);

        frameBufferStack.Clear();

        // Fresh frame state: main shader, alpha blend, no active mask.
        currentShader = mainShader;
        currentBlendMode = BlendingMode.Alpha;

        clipStack.Clear();
        currentClip = new ClipState
        {
            ClipData = new Vector4(0, 0, -1, -1),
            ShearX = 0,
            Radius = 0,
        };
        maskState = default;

        // Bind the pipeline + projection + mask for this frame's (drawable) encoder.
        rebindFrameState();
    }

    /// <summary>
    /// Re-establishes all encoder-resident state for the current render target. A render pass switch
    /// (begin_frame / begin_offscreen / end_offscreen) opens a brand-new native encoder, which loses
    /// the pipeline, uniform buffers and texture bindings — so this must run after every switch.
    /// </summary>
    private void rebindFrameState()
    {
        bindPipeline();
        uploadProjection();
        uploadMaskState();

        // Bind the white pixel to slot 0 so solid-color drawables (TexIndex 0) sample white.
        (WhitePixel?.BackendTexture as MetalTexture)?.Bind(0);
    }

    // Binds the pipeline variant for the current (shader, blend mode) pair to the encoder.
    private void bindPipeline()
    {
        if (currentShader != null)
            SakuraMetalNative.sakura_metal_set_pipeline(device, currentShader.GetPipeline(currentBlendMode));
    }

    private unsafe void uploadProjection()
    {
        var block = new ProjectionBlock { Projection = projectionMatrix };
        SakuraMetalNative.sakura_metal_set_vertex_uniform(device, &block, sizeof(ProjectionBlock), projection_buffer_index);
    }

    private unsafe void uploadMaskState()
    {
        fixed (MaskBlock* ptr = &maskState)
            SakuraMetalNative.sakura_metal_set_fragment_uniform(device, ptr, sizeof(MaskBlock), mask_buffer_index);
    }

    public void SetRoot(DrawNode rootDrawNode) => rootNode = rootDrawNode;

    public void Resize(int physicalWidth, int physicalHeight, int logicalWidth, int logicalHeight)
    {
        if (device == nint.Zero)
            return;

        renderScaleX = (float)physicalWidth / logicalWidth;
        renderScaleY = (float)physicalHeight / logicalHeight;

        SakuraMetalNative.sakura_metal_resize(device, physicalWidth, physicalHeight, renderScaleX);

        // The cross-compiled MSL vertex shader already negates clip-space Y for Metal. So unlike the
        // GL path (which uses top=h, bottom=0), we map top=0, bottom=h here; the shader's Y-flip then
        // lands the top-left origin correctly. Feeding GL's projection would double-flip (upside down).
        projectionMatrix = Matrix4x4.CreateOrthographicOffCenter(0, logicalWidth, 0, logicalHeight, -1, 1);
    }

    public void Draw(IClock clock)
    {
        if (device == nint.Zero)
            return;

        rootNode?.Draw(this);

        SakuraMetalNative.sakura_metal_end_frame(device);
    }

    /// <summary>
    /// Scratch buffer for forcing TexIndex to 0 on the drawn vertices (see below). Reused across draws.
    /// </summary>
    private SakuraVertex[] drawScratch = new SakuraVertex[256];

    public unsafe void DrawVertices(ReadOnlySpan<SakuraVertex> vertices, Texture texture)
    {
        if (device == nint.Zero || vertices.Length == 0)
            return;

        // One texture per draw on Metal (no multi-texture batching yet): bind it to slot 0.
        var native = (texture?.BackendTexture ?? WhitePixel.BackendTexture) as MetalTexture;
        native?.Bind(0);

        // The vertices may carry a TexIndex chosen for the GL batch's slot assignment. Since we bind
        // a single texture to slot 0, force TexIndex 0 so the shader samples the texture we bound,
        // not an unbound slot. Copy into a reusable scratch buffer to avoid mutating the caller's span.
        //
        // We also inject the current clip state into each vertex here. Unlike GL — where TriangleBatch
        // writes currentClip.ClipData into every vertex on its way into the batch — the Metal path has
        // no batch, so the draw nodes hand over vertices with default (zero) clip data. The fragment
        // shader's applyClipping reads these per-vertex attributes, so without this injection masking
        // would silently do nothing. ClipData (0,0,-1,-1) means "no active clip" to the shader.
        if (drawScratch.Length < vertices.Length)
            drawScratch = new SakuraVertex[Math.Max(vertices.Length, drawScratch.Length * 2)];

        for (int i = 0; i < vertices.Length; i++)
        {
            drawScratch[i] = vertices[i];
            drawScratch[i].TexIndex = 0f;
            drawScratch[i].ClipData = currentClip.ClipData;
            drawScratch[i].ClipShearX = currentClip.ShearX;
            drawScratch[i].ClipRadius = currentClip.Radius;
        }

        fixed (SakuraVertex* ptr = drawScratch)
            SakuraMetalNative.sakura_metal_draw_triangles(device, ptr, vertices.Length, SakuraVertex.Size);
    }

    public void DrawQuads(ReadOnlySpan<SakuraVertex> vertices, Texture texture)
    {
        // Expand each quad (TL, TR, BR, BL) into two triangles, since the bridge draws triangle lists.
        Span<SakuraVertex> tri = stackalloc SakuraVertex[6];
        for (int i = 0; i + 4 <= vertices.Length; i += 4)
        {
            tri[0] = vertices[i];
            tri[1] = vertices[i + 1];
            tri[2] = vertices[i + 2];
            tri[3] = vertices[i + 2];
            tri[4] = vertices[i + 3];
            tri[5] = vertices[i];
            DrawVertices(tri, texture);
        }
    }

    #region Masking / borders

    /// <summary>
    /// Pushes a clip region. Mirrors <c>GLRenderer.PushMask</c>: the new mask is intersected (as an
    /// AABB, accounting for horizontal shear) with any parent mask, and the result becomes the active
    /// clip carried into subsequent vertices by <see cref="DrawVertices"/>.
    /// </summary>
    public void PushMask(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius)
    {
        clipStack.Push(currentClip);

        // True AABB of this new mask, taking horizontal shear into account.
        float skewOffset = Math.Abs(shearX * maskHalfSize.Y);
        float left = maskCenter.X - maskHalfSize.X - skewOffset;
        float right = maskCenter.X + maskHalfSize.X + skewOffset;
        float top = maskCenter.Y - maskHalfSize.Y;
        float bottom = maskCenter.Y + maskHalfSize.Y;

        // If already inside a parent mask (Z > 0), intersect their bounding boxes.
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

        // If the intersection collapses (child entirely outside the parent mask), shrink to ~zero so
        // the shader discards all fragments.
        if (left >= right || top >= bottom)
        {
            newHalfSize = new Vector2(0.0001f, 0.0001f);
        }
        else
        {
            // Remove the skew offset again so the shader receives the true un-sheared half-size.
            newHalfSize.X = Math.Max(0.0001f, newHalfSize.X - skewOffset);
        }

        currentClip = new ClipState
        {
            ClipData = new Vector4(newCenter.X, newCenter.Y, newHalfSize.X, newHalfSize.Y),
            ShearX = shearX,
            Radius = cornerRadius,
        };
    }

    /// <summary>
    /// Pops the clip region pushed by the matching <see cref="PushMask"/> and draws the container's
    /// border on top, if any. Mirrors <c>GLRenderer.PopMask</c>.
    /// </summary>
    public void PopMask(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius, float borderThickness, Color borderColor, ReadOnlySpan<SakuraVertex> maskVertices = default)
    {
        currentClip = clipStack.Count > 0 ? clipStack.Pop() : currentClip;
        drawBorder(maskCenter, maskHalfSize, shearX, cornerRadius, borderThickness, borderColor, maskVertices);
    }

    /// <summary>
    /// Draws the rounded/sheared border quad via the main shader's border path (u_IsBorder). The
    /// border color and geometry travel through the MaskBlock UBO at fragment [[buffer(1)]]; the
    /// fragment shader computes the border ring from the signed-distance field. Mirrors
    /// <c>GLRenderer.drawBorder</c>, minus the GL-only batch/texture-slot bookkeeping.
    /// </summary>
    private void drawBorder(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius, float borderThickness, Color borderColor, ReadOnlySpan<SakuraVertex> vertices)
    {
        if (borderThickness <= 0 || vertices.Length < 4)
            return;

        // Enter the border pass: populate and upload the MaskBlock the fragment shader reads.
        maskState.IsBorder = 1;
        maskState.MaskCenter = new Vector2(maskCenter.X, maskCenter.Y);
        maskState.MaskHalfSize = new Vector2(maskHalfSize.X, maskHalfSize.Y);
        maskState.ShearX = shearX;
        maskState.CornerRadius = cornerRadius;
        maskState.BorderThickness = borderThickness;
        maskState.BorderColor = new Vector4(
            borderColor.R / 255f, borderColor.G / 255f, borderColor.B / 255f, borderColor.A / 255f);
        uploadMaskState();

        // The border samples the white pixel (texColor is ignored in the border path, but the shader
        // still indexes u_Textures[0]); the white pixel is already bound to slot 0 for the frame.
        // Draw the single mask quad (TL, TR, BR, BL). The border ring is shaded by the SDF, and the
        // current clip is applied per-vertex (the border honours any enclosing parent mask).
        DrawQuads(vertices[..4], WhitePixel);

        // Leave the border pass so subsequent draws render normally.
        maskState.IsBorder = 0;
        uploadMaskState();
    }

    /// <summary>
    /// Draws a soft edge effect (glow/shadow) via the main shader's edge-effect path
    /// (u_IsEdgeEffect). Mirrors <c>GLRenderer.DrawEdgeEffect</c>; clip data is injected into the
    /// quad vertices by <see cref="DrawQuads"/>, so it is not applied here.
    /// </summary>
    public void DrawEdgeEffect(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius, float edgeRadius, Vector2 offset, Color color, bool glow, bool hollow, ReadOnlySpan<SakuraVertex> quadVertices)
    {
        if (color.A == 0 || quadVertices.Length < 4)
            return;

        var previousBlend = currentBlendMode;
        if (glow)
            SetBlendMode(BlendingMode.Additive);

        maskState.IsEdgeEffect = 1;
        maskState.MaskCenter = new Vector2(maskCenter.X, maskCenter.Y);
        maskState.MaskHalfSize = new Vector2(maskHalfSize.X, maskHalfSize.Y);
        maskState.ShearX = shearX;
        maskState.CornerRadius = cornerRadius;
        maskState.EdgeRadius = edgeRadius;
        maskState.EdgeOffset = new Vector2(offset.X, offset.Y);
        maskState.EdgeHollow = hollow ? 1 : 0;
        maskState.EdgeGlow = glow ? 1 : 0;
        maskState.BorderColor = new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
        uploadMaskState();

        DrawQuads(quadVertices[..4], WhitePixel);

        maskState.IsEdgeEffect = 0;
        uploadMaskState();

        if (glow)
            SetBlendMode(previousBlend);
    }

    #endregion

    #region Blend state

    /// <summary>
    /// Selects the blend mode for subsequent draws by binding the matching pipeline variant of the
    /// current shader (Metal bakes blend into the pipeline). Mirrors <c>GLRenderer.SetBlendMode</c>.
    /// </summary>
    public void SetBlendMode(BlendingMode blendingMode)
    {
        if (blendingMode == currentBlendMode)
            return;

        currentBlendMode = blendingMode;
        bindPipeline();
    }

    #endregion

    #region Framebuffers (offscreen render targets)

    public IFrameBuffer CreateFrameBuffer(int width, int height, bool pixelSnapping = false)
        => new MetalFrameBuffer(device, width, height);

    /// <summary>
    /// Begins rendering into <paramref name="frameBuffer"/>. Saves the current projection + clip,
    /// remaps the projection so children render with their unchanged screen-space coordinates onto the
    /// buffer (same convention as <c>GLRenderer.BindFrameBuffer</c>), opens the native offscreen pass,
    /// and re-binds all encoder state into the new pass.
    /// </summary>
    public void BindFrameBuffer(IFrameBuffer frameBuffer, RectangleF sourceRect, Color clearColor = default)
    {
        if (device == nint.Zero || frameBuffer is not MetalFrameBuffer metalFrameBuffer)
            return;

        frameBufferStack.Push(new FrameBufferState
        {
            Projection = projectionMatrix,
            Clip = currentClip,
        });

        // Map the captured logical screen-space rect onto the buffer using GL's FB orientation
        // (bottom = rect.Y + height, top = rect.Y), the "inverse" of the window projection.
        //
        // Why inverted: the cross-compiled MSL vertex shader applies InvertVertexOutputY on "every" pass,
        // including offscreen ones (the flip is baked into the shader, not tied to the drawable). GL has
        // no such shader flip, so the BufferedContainer pipeline, which reads every buffer with
        // V-flipped UVs (BufferedContainerDrawNode.fillQuad) and chains content -> blur -> grayscale
        // is tuned assuming zero shader flips. Inverting the offscreen projection here cancels the
        // shader's per-pass flip, making each Metal offscreen pass behave exactly like GL's. This keeps
        // the whole shared GL-tuned chain correct for any number of passes.
        //
        // (An earlier version used the window convention; it rendered buffered content upside down with
        // no effect, but upright with grayscale — the tell that the flip was per-pass and parity-
        // dependent. Inverting the projection fixes both, because it removes the per-pass flip itself.)
        projectionMatrix = Matrix4x4.CreateOrthographicOffCenter(
            sourceRect.X, sourceRect.X + sourceRect.Width,
            sourceRect.Y + sourceRect.Height, sourceRect.Y,
            -1, 1);

        // Content inside the buffer starts from a clean clip state; the outer clip applies to the
        // final composited quad instead.
        currentClip = new ClipState
        {
            ClipData = new Vector4(0, 0, -1, -1),
            ShearX = 0,
            Radius = 0,
        };

        SakuraMetalNative.sakura_metal_begin_offscreen(
            device, metalFrameBuffer.TextureHandle,
            clearColor.R / 255f, clearColor.G / 255f, clearColor.B / 255f, clearColor.A / 255f);

        // The offscreen pass opened a fresh encoder, re-establish pipeline/projection/mask/texture.
        rebindFrameState();
    }

    public void UnbindFrameBuffer()
    {
        if (device == nint.Zero || frameBufferStack.Count == 0)
            throw new InvalidOperationException($"{nameof(UnbindFrameBuffer)} was called without a matching {nameof(BindFrameBuffer)}.");

        SakuraMetalNative.sakura_metal_end_offscreen(device);

        var state = frameBufferStack.Pop();
        projectionMatrix = state.Projection;
        currentClip = state.Clip;

        // The parent pass was reopened with a fresh encoder (LOAD action) — re-establish all state.
        rebindFrameState();
    }

    #endregion

    #region Custom shader

    /// <summary>
    /// Compiles a custom shader pair to MSL and builds a <see cref="MetalShader"/>. Used for the
    /// buffered-container blur/grayscale effect shaders. The vertex layout matches the standard Vertex
    /// struct (the effect shaders pair with shader.vert).
    /// </summary>
    public IShader CreateShader(Storage storage, string vertexPath, string fragmentPath)
    {
        var (vertMsl, fragMsl) = ShaderCompiler.GetOrCompile(
            storage, vertexPath, fragmentPath, SPIRV.CrossCompileTarget.MSL, ShaderCache);

        return new MetalShader(device, vertMsl, fragMsl, buildVertexAttributes(), SakuraVertex.Size, customShaderUniformBindings(), owner: this);
    }

    /// <summary>
    /// Binds <paramref name="shader"/> as the current shader (so its pipeline variant is used for
    /// subsequent draws + blend switches) and binds its current-blend pipeline. The caller uploads the
    /// shader's uniform blocks via <see cref="IShader.SetUniformBlock{T}"/> after this.
    /// </summary>
    internal void UseShader(MetalShader shader)
    {
        currentShader = shader;
        bindPipeline();
    }

    /// <summary>
    /// Restores the main shader as the current shader (after a custom-shader effect pass) and re-binds
    /// the neutral mask/projection. Mirrors <c>GLRenderer.RestoreMainShader</c>.
    /// </summary>
    public void RestoreMainShader()
    {
        currentShader = mainShader;
        bindPipeline();
        uploadProjection();

        maskState.IsMasking = 0;
        maskState.IsBorder = 0;
        uploadMaskState();
    }

    /// <summary>
    /// Raw triangle draw with the currently-bound pipeline, without clip injection or texture-slot
    /// bookkeeping. The effect passes bind their own shader + source texture first. The vertices are
    /// drawn as two triangles per quad (the bridge takes a triangle list).
    /// </summary>
    public unsafe void DrawVerticesRaw(ReadOnlySpan<SakuraVertex> vertices)
    {
        if (device == nint.Zero || vertices.Length == 0)
            return;

        if (vertices.Length == 4)
        {
            // Expand the quad (TL, TR, BR, BL) into two triangles.
            Span<SakuraVertex> tri = stackalloc SakuraVertex[6];
            tri[0] = vertices[0];
            tri[1] = vertices[1];
            tri[2] = vertices[2];
            tri[3] = vertices[2];
            tri[4] = vertices[3];
            tri[5] = vertices[0];

            fixed (SakuraVertex* ptr = tri)
                SakuraMetalNative.sakura_metal_draw_triangles(device, ptr, 6, SakuraVertex.Size);
            return;
        }

        fixed (SakuraVertex* ptr = vertices)
            SakuraMetalNative.sakura_metal_draw_triangles(device, ptr, vertices.Length, SakuraVertex.Size);
    }

    #endregion

    #region Utilities

    public void ScheduleToDrawThread(Action action) => drawThreadQueue.Enqueue(action);

    public void FlushBatch()
    {
        // Metal draws immediately (no batch), so there's nothing buffered to flush.
    }

    public INativeVideoTexture CreateVideoTexture(int width, int height) => new MetalVideoTexture(device, width, height);
    public INativeTexture CreateNativeTexture(int width, int height) => new MetalTexture(device, width, height);

    #endregion
}
