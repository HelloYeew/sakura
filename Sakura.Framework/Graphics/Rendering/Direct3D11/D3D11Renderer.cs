// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Sakura.Framework.Graphics.Rendering.Batches;
using Sakura.Framework.Graphics.Rendering.Uniforms;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.IO;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Statistic;
using Sakura.Framework.Timing;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D11.D3D11;
using Color = Sakura.Framework.Graphics.Colors.Color;
using SakuraVertex = Sakura.Framework.Graphics.Rendering.Vertex.Vertex;

namespace Sakura.Framework.Graphics.Rendering.Direct3D11;

/// <summary>
/// Direct3D 11 renderer backend (managed, via Vortice.Windows).
/// </summary>
public sealed class D3D11Renderer : ID3D11Renderer, IDisposable
{
    private static readonly GlobalStatistic<int> stat_draw_calls = GlobalStatistics.Get<int>("Renderer", "Draw Calls");
    private static readonly GlobalStatistic<int> stat_vertices_drawn = GlobalStatistics.Get<int>("Renderer", "Vertices Drawn");
    private static readonly GlobalStatistic<int> stat_slot_exhaustion_flushes = GlobalStatistics.Get<int>("Renderer", "Slot Exhaustion Flushes");
    private static readonly GlobalStatistic<int> stat_state_change_flushes = GlobalStatistics.Get<int>("Renderer", "State Change Flushes");

    /// <summary>
    /// The live renderer instance, for static notifications (texture deletion). Effectively a
    /// singleton, only one D3D11 renderer exists per process.
    /// </summary>
    private static D3D11Renderer instance;

    /// <summary>
    /// Must be called whenever a texture's shader resource view is released. COM addresses are
    /// recycled, so a new SRV can land on the address of a dead one and if the slot mirror still maps
    /// that address to a slot, the renderer would skip the bind and draw with whatever now occupies it.
    /// Draw thread only, same as the release itself.
    /// </summary>
    internal static void NotifyTextureDeleted(nint handle)
    {
        var renderer = instance;
        if (renderer == null || handle == nint.Zero)
            return;

        for (int i = 0; i < renderer.boundTextureHandles.Length; i++)
        {
            if (renderer.boundTextureHandles[i] == handle)
                renderer.boundTextureHandles[i] = -1; // never matches a live SRV
        }
    }

    private ID3D11Device device;
    private ID3D11DeviceContext context;

    private IDXGISwapChain1 swapChain;
    private IDXGISwapChain2 waitableSwapChain;
    private ID3D11RenderTargetView backBufferRtv;
    private int backBufferWidth;
    private int backBufferHeight;

    // The swapchain is created waitable with a max frame latency of 1,
    // the draw thread waits on this handle at the top of each frame so CPU work aligns with the
    // display and input-to-photon latency stays low.
    private nint frameLatencyWaitableObject;

    // Whether the output supports tearing (required for uncapped/no-VSync present via AllowTearing).
    private bool allowTearing;

    // The flags the swapchain was created with, must be reused on ResizeBuffers.
    private SwapChainFlags swapChainFlags = SwapChainFlags.FrameLatencyWaitableObject;

    // Present sync interval: 1 = VSync (default, safe), 0 = uncapped. Driven by the framework frame
    // limiter through SetVSync. See Draw for the interval/flag mapping.
    private int presentSyncInterval = 1;

    private nint windowHandle;

    private readonly ConcurrentQueue<Action> drawThreadQueue = new ConcurrentQueue<Action>();
    private readonly TextureUploadQueue textureUploadQueue = new TextureUploadQueue();

    private DrawNode rootNode;
    private Matrix4x4 projectionMatrix = Matrix4x4.Identity;

    private float renderScaleX = 1.0f;
    private float renderScaleY = 1.0f;

    private static readonly Color4 clear_color = new Color4(0f, 0f, 0f, 1f);

    // Register mapping from the sakura-spirv HLSL cross-compile (resources ordered by (set,binding),
    // per-kind counters): ProjectionBlock -> VS b0, MaskBlock -> PS b1, u_Textures[16] -> PS t0..t15 / s0.
    private const int projection_cb_slot = 0;
    private const int mask_cb_slot = 1;

    // Matches u_Textures[] in shader.frag: the batch assigns a slot per distinct texture and stamps the
    // index into every vertex. All 16 are padded with the white SRV at frame start so the shader's
    // declared array is fully bound whatever the batch ends up using.
    private const int texture_slot_count = 16;

    /// <summary>
    /// Vertex capacity of <see cref="batch"/>, matching the GL batch (12000 vertices), i.e., 3000 quads
    /// (indexed, 4 vertices each) before a capacity flush.
    /// </summary>
    private const int max_batch_vertices = 1000 * 12;

    /// <summary>
    /// CPU mirror of the pixel stage's shader-resource slots, dense from 0. The pixel-stage bindings
    /// survive a draw and a render-target switch, so this only needs clearing when something binds
    /// behind the renderer's back (the raw custom-shader passes, and <see cref="rebindFrameState"/>).
    /// </summary>
    private readonly nint[] boundTextureHandles = new nint[texture_slot_count];
    private int boundTextureCount;

    /// <summary>
    /// The frame's pending geometry, indexed (see <see cref="quadIndexBuffer"/>).
    /// </summary>
    private VertexBatch batch;

    private D3D11Shader mainShader;
    private D3D11Shader currentShader;
    private BlendingMode currentBlendMode = BlendingMode.Alpha;

    private ID3D11Buffer projectionCb;
    private ID3D11Buffer maskCb;

    private readonly ID3D11BlendState[] blendStates = new ID3D11BlendState[6];
    private ID3D11RasterizerState rasterizerState;
    private ID3D11SamplerState linearClampSampler;
    private ID3D11DepthStencilState depthStencilOff;

    // Dynamic vertex buffer (grown on demand), mapped WRITE_DISCARD per draw.
    private ID3D11Buffer vertexBuffer;
    private int vertexBufferCapacity;

    /// <summary>
    /// Immutable index buffer holding the quad pattern (0,1,2, 2,3,0 per quad) for the whole batch
    /// capacity, mirroring GL's <c>quadEbo</c>. A batch of nothing but quads draws straight from this,
    /// so the common flush uploads vertices only.
    /// </summary>
    private ID3D11Buffer quadIndexBuffer;

    /// <summary>
    /// Dynamic index buffer for a batch that contains a triangle list (see
    /// <see cref="VertexBatch.HasNonQuad"/>), whose indices don't follow the static quad pattern.
    /// </summary>
    private ID3D11Buffer dynamicIndexBuffer;

    // White-pixel SRVs bound to all texture_slot_count slots in one call, so the shader's u_Textures[]
    // array is fully bound whatever the batch ends up assigning. Note this padding is not counted by
    // TextureBindTracker (it doesn't go through D3D11Texture.Bind), unlike Metal's equivalent — so the
    // two backends' bind figures aren't directly comparable.
    private ID3D11ShaderResourceView[] whiteSrvs;

    // No-clip state injected into every vertex, PushMask/PopMask maintain the stack.
    private ClipState currentClip = ClipState.None;
    private readonly Stack<ClipState> clipStack = new();

    private MaskBlock maskState;

    // Currently-bound render target + viewport, so BindFrameBuffer can save/restore across nesting.
    private ID3D11RenderTargetView currentRtv;
    private int currentViewportW;
    private int currentViewportH;
    private readonly Stack<FrameBufferState> frameBufferStack = new();

    private readonly struct ClipState
    {
        public readonly Vector4 ClipData;
        public readonly float ShearX;
        public readonly float Radius;

        public ClipState(Vector4 clipData, float shearX, float radius)
        {
            ClipData = clipData;
            ShearX = shearX;
            Radius = radius;
        }

        // (0,0,-1,-1) means "no active clip" to the fragment shader's applyClipping.
        public static ClipState None => new ClipState(new Vector4(0, 0, -1, -1), 0, 0);
    }

    private readonly struct FrameBufferState
    {
        public readonly ID3D11RenderTargetView Rtv;
        public readonly int ViewportW;
        public readonly int ViewportH;
        public readonly Matrix4x4 Projection;
        public readonly ClipState Clip;

        public FrameBufferState(ID3D11RenderTargetView rtv, int viewportW, int viewportH, Matrix4x4 projection, ClipState clip)
        {
            Rtv = rtv;
            ViewportW = viewportW;
            ViewportH = viewportH;
            Projection = projection;
            Clip = clip;
        }
    }

    public Texture WhitePixel { get; private set; }

    public Storage ShaderStorage { get; set; }

    public DiskCache ShaderCache { get; set; }

    public Matrix4x4 ProjectionMatrix => projectionMatrix;

    public Vector2 RenderScale => new Vector2(renderScaleX, renderScaleY);

    public void Initialize(IGraphicsSurface graphicsSurface)
    {
        if (graphicsSurface is not IWin32GraphicsSurface win32Surface)
            throw new InvalidOperationException($"{nameof(D3D11Renderer)} requires an {nameof(IWin32GraphicsSurface)}.");

        instance = this;
        windowHandle = win32Surface.WindowHandle;

        var flags = DeviceCreationFlags.BgraSupport;
#if DEBUG
        flags |= DeviceCreationFlags.Debug;
#endif

        FeatureLevel[] featureLevels = { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 };

        if (!tryCreateDevice(flags, featureLevels))
        {
#if DEBUG
            Logger.Verbose("D3D11 device creation with the debug layer failed; retrying without it.");
            if (!tryCreateDevice(flags & ~DeviceCreationFlags.Debug, featureLevels))
#endif
                throw new InvalidOperationException("Failed to create the Direct3D 11 device.");
        }

        logDeviceInfo();
        createSwapChain();

        createStateObjects();
        createConstantBuffers();
        createMainShader();
        createWhitePixel();
        createBatch();
    }

    private bool tryCreateDevice(DeviceCreationFlags flags, FeatureLevel[] featureLevels)
    {
        var result = D3D11CreateDevice(null, DriverType.Hardware, flags, featureLevels, out device, out context);
        return result.Success && device != null;
    }

    public static bool IsSupported()
    {
        try
        {
            FeatureLevel[] featureLevels = { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 };
            var result = D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
                featureLevels, out ID3D11Device probeDevice, out ID3D11DeviceContext probeContext);

            probeContext?.Dispose();
            probeDevice?.Dispose();
            return result.Success && probeDevice != null;
        }
        catch
        {
            return false;
        }
    }

    private void logDeviceInfo()
    {
        try
        {
            using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
            using IDXGIAdapter adapter = dxgiDevice.GetAdapter();
            AdapterDescription desc = adapter.Description;
            Logger.Verbose($"🖥️ Direct3D11 adapter: {desc.Description?.Trim()} (feature level {device.FeatureLevel})");
        }
        catch (Exception e)
        {
            Logger.Verbose($"Direct3D11 device created; adapter info unavailable ({e.Message}).");
        }
    }

    private void createSwapChain()
    {
        using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
        using IDXGIAdapter adapter = dxgiDevice.GetAdapter();
        using IDXGIFactory2 factory = adapter.GetParent<IDXGIFactory2>();

        allowTearing = queryTearingSupport(factory);

        // Waitable for low latency; AllowTearing (if supported) so an uncapped/no-VSync present can
        // tear instead of stalling. These are fixed for the swapchain's lifetime and reused on resize.
        swapChainFlags = SwapChainFlags.FrameLatencyWaitableObject;
        if (allowTearing)
            swapChainFlags |= SwapChainFlags.AllowTearing;

        var desc = new SwapChainDescription1
        {
            Width = 0,
            Height = 0,
            Format = Format.B8G8R8A8_UNorm,
            Stereo = false,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = 2,
            Scaling = Scaling.None,
            SwapEffect = SwapEffect.FlipDiscard,
            AlphaMode = AlphaMode.Ignore,
            Flags = swapChainFlags,
        };

        swapChain = factory.CreateSwapChainForHwnd(device, windowHandle, desc);
        factory.MakeWindowAssociation(windowHandle, WindowAssociationFlags.IgnoreAltEnter);

        // Retrieve the waitable object and cap queued frames to 1 (the low-latency path).
        waitableSwapChain = swapChain.QueryInterfaceOrNull<IDXGISwapChain2>();
        if (waitableSwapChain != null)
        {
            waitableSwapChain.MaximumFrameLatency = 1;
            frameLatencyWaitableObject = waitableSwapChain.FrameLatencyWaitableObject;
        }

        createBackBufferView();
    }

    /// <summary>
    /// Queries whether the output supports tearing (needed for uncapped / no-VSync present). Falls back
    /// to <c>false</c> (VSync-only, no tearing) if the feature query is unavailable.
    /// </summary>
    private static bool queryTearingSupport(IDXGIFactory2 factory)
    {
        try
        {
            using var factory5 = factory.QueryInterfaceOrNull<IDXGIFactory5>();
            return factory5 != null && factory5.PresentAllowTearing;
        }
        catch
        {
            return false;
        }
    }

    private void createBackBufferView()
    {
        using ID3D11Texture2D backBuffer = swapChain.GetBuffer<ID3D11Texture2D>(0);

        var rtvDesc = new RenderTargetViewDescription
        {
            Format = Format.B8G8R8A8_UNorm_SRgb,
            ViewDimension = RenderTargetViewDimension.Texture2D,
        };

        backBufferRtv = device.CreateRenderTargetView(backBuffer, rtvDesc);

        Texture2DDescription td = backBuffer.Description;
        backBufferWidth = (int)td.Width;
        backBufferHeight = (int)td.Height;
    }

    #region Startup: state objects, buffers, shader, white pixel

    private void createStateObjects()
    {
        // Note: D3D forbids the *_Color blend factors on the alpha channel, so Multiply's DestColor
        // and Screen's InverseSourceColor can't be replicated on alpha, those keep a valid
        // approximation (only affects direct rendering, where the backbuffer alpha is unused).
        blendStates[(int)BlendingMode.Alpha] = createBlend(Blend.SourceAlpha, Blend.InverseSourceAlpha, Blend.SourceAlpha, Blend.InverseSourceAlpha);
        blendStates[(int)BlendingMode.Additive] = createBlend(Blend.SourceAlpha, Blend.One, Blend.SourceAlpha, Blend.One);
        blendStates[(int)BlendingMode.Opaque] = createBlend(Blend.One, Blend.Zero, Blend.One, Blend.Zero);
        blendStates[(int)BlendingMode.Multiply] = createBlend(Blend.DestinationColor, Blend.InverseSourceAlpha, Blend.One, Blend.InverseSourceAlpha);
        blendStates[(int)BlendingMode.Screen] = createBlend(Blend.One, Blend.InverseSourceColor, Blend.One, Blend.InverseSourceAlpha);
        blendStates[(int)BlendingMode.Premultiplied] = createBlend(Blend.One, Blend.InverseSourceAlpha, Blend.One, Blend.InverseSourceAlpha);

        rasterizerState = device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid));

        linearClampSampler = device.CreateSamplerState(new SamplerDescription
        {
            Filter = Filter.MinMagMipLinear,
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
            ComparisonFunc = ComparisonFunction.Never,
            MinLOD = 0,
            MaxLOD = float.MaxValue,
        });

        depthStencilOff = device.CreateDepthStencilState(new DepthStencilDescription
        {
            DepthEnable = false,
            DepthWriteMask = DepthWriteMask.Zero,
            DepthFunc = ComparisonFunction.Always,
            StencilEnable = false,
        });
    }

    private ID3D11BlendState createBlend(Blend src, Blend dest, Blend srcA, Blend destA)
    {
        var desc = new BlendDescription();
        desc.RenderTarget[0] = new RenderTargetBlendDescription
        {
            BlendEnable = true,
            SourceBlend = src,
            DestinationBlend = dest,
            BlendOperation = BlendOperation.Add,
            SourceBlendAlpha = srcA,
            DestinationBlendAlpha = destA,
            BlendOperationAlpha = BlendOperation.Add,
            RenderTargetWriteMask = ColorWriteEnable.All,
        };
        return device.CreateBlendState(desc);
    }

    private void createConstantBuffers()
    {
        projectionCb = device.CreateBuffer(new BufferDescription(
            (uint)Marshal.SizeOf<ProjectionBlock>(), BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));

        maskCb = device.CreateBuffer(new BufferDescription(
            (uint)Marshal.SizeOf<MaskBlock>(), BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
    }

    private void createMainShader()
    {
        var (vertHlsl, fragHlsl) = ShaderCompiler.GetOrCompile(
            ShaderStorage, "shader.vert", "shader.frag", SPIRV.CrossCompileTarget.HLSL, ShaderCache);

        mainShader = new D3D11Shader(device, context, vertHlsl, fragHlsl, buildInputElements());
        currentShader = mainShader;
    }

    /// <summary>
    /// Input layout matching the interleaved <see cref="SakuraVertex"/> struct. Semantic is
    /// <c>TEXCOORD{location}</c> (SPIRV-Cross's default for HLSL vertex inputs) the byte offset comes
    /// from the struct field (locations and field order differ, see shader.vert).
    /// </summary>
    private static InputElementDescription[] buildInputElements() =>
    [
        element(0, Format.R32G32_Float, nameof(SakuraVertex.Position)),
        element(1, Format.R32G32_Float, nameof(SakuraVertex.TexCoords)),
        element(2, Format.R32G32B32A32_Float, nameof(SakuraVertex.Color)),
        element(3, Format.R32_Float, nameof(SakuraVertex.TexIndex)),
        element(4, Format.R32G32B32A32_Float, nameof(SakuraVertex.ClipData)),
        element(5, Format.R32_Float, nameof(SakuraVertex.ClipShearX)),
        element(6, Format.R32_Float, nameof(SakuraVertex.ClipRadius)),
    ];

    private static InputElementDescription element(int location, Format format, string field) =>
        new InputElementDescription("TEXCOORD", (uint)location, format, (uint)Marshal.OffsetOf<SakuraVertex>(field), 0);

    private void createWhitePixel()
    {
        var white = new D3D11Texture(device, context, 1, 1);
        white.Upload(new byte[] { 255, 255, 255, 255 });
        D3D11Texture.WhitePixel = white;
        WhitePixel = new Texture(white, TextureOwnership.Shared);

        whiteSrvs = new ID3D11ShaderResourceView[texture_slot_count];
        for (int i = 0; i < texture_slot_count; i++)
            whiteSrvs[i] = white.ShaderResourceView;
    }

    private unsafe void createBatch()
    {
        batch = new VertexBatch(max_batch_vertices, FlushBatch);

        // Sized to the batch's capacity so a flush never has to reallocate.
        ensureVertexCapacity(max_batch_vertices);

        dynamicIndexBuffer = device.CreateBuffer(new BufferDescription(
            (uint)(batch.MaxIndices * sizeof(uint)), BindFlags.IndexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));

        // The static quad pattern, filled once. Immutable: the contents are the same every frame, so an
        // all-quad batch (the overwhelming majority) uploads vertices only.
        int maxQuads = max_batch_vertices / 4;
        uint[] quadIndices = new uint[maxQuads * 6];
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

        quadIndexBuffer = device.CreateBuffer(quadIndices, new BufferDescription(
            (uint)(quadIndices.Length * sizeof(uint)), BindFlags.IndexBuffer, ResourceUsage.Immutable));
    }

    #endregion

    public void Clear()
    {
        // Folded into StartFrame (the RTV is cleared there once it's bound).
    }

    public void StartFrame()
    {
        if (device == null || swapChain == null)
            return;

        // Low-latency pacing: block until the swapchain is ready for a new frame (max latency 1), so
        // the CPU doesn't run ahead of the display. Timeout-guarded so a lost/uninitialised handle can
        // never hang the draw thread. Initial state is signalled, so the first wait returns at once.
        if (frameLatencyWaitableObject != nint.Zero)
            WaitForSingleObjectEx(frameLatencyWaitableObject, 1000, true);

        // Release native resources orphaned by the GC (a missed Dispose) before anything else this
        // frame. D3D11's own resources are COM objects that SharpGen already finalizes, so in practice
        // this only drains cross-backend enqueues, but it keeps frame start uniform across renderers.
        Textures.NativeDisposalQueue.Process();

        while (drawThreadQueue.TryDequeue(out var action))
            action();

        // Budgeted texture uploads spread a burst across frames (see TextureUploadQueue).
        textureUploadQueue.Process();

        frameBufferStack.Clear();
        setRenderTarget(backBufferRtv, backBufferWidth, backBufferHeight);
        context.ClearRenderTargetView(backBufferRtv, clear_color);

        currentShader = mainShader;
        currentBlendMode = BlendingMode.Alpha;
        currentClip = ClipState.None;
        clipStack.Clear();
        maskState = default;

        rebindFrameState();
    }

    /// <summary>
    /// Binds a render target + full-surface viewport and records them, so <see cref="BindFrameBuffer"/> can
    /// save/restore across nested offscreen passes.
    /// </summary>
    private void setRenderTarget(ID3D11RenderTargetView rtv, int width, int height)
    {
        currentRtv = rtv;
        currentViewportW = width;
        currentViewportH = height;
        context.OMSetRenderTargets(rtv);
        context.RSSetViewport(0, 0, width, height);
    }

    /// <summary>
    /// Reestablishes all pipeline state for the current render target: shader, input layout,
    /// topology, constant buffers, sampler, textures, blend / rasterizer / depth-stencil state.
    /// </summary>
    private void rebindFrameState()
    {
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        currentShader.Use();

        context.VSSetConstantBuffer(projection_cb_slot, projectionCb);
        context.PSSetConstantBuffer(mask_cb_slot, maskCb);
        context.PSSetSampler(0, linearClampSampler);

        // Every declared slot gets a valid view; the batch overwrites the ones it assigns.
        context.PSSetShaderResources(0, whiteSrvs);
        resetTextureSlotsToWhite();

        context.RSSetState(rasterizerState);
        context.OMSetDepthStencilState(depthStencilOff, 0);
        context.OMSetBlendState(blendStates[(int)currentBlendMode]);

        uploadProjection();
        uploadMaskState();
    }

    private void uploadProjection()
    {
        var block = new ProjectionBlock { Projection = projectionMatrix };
        updateConstantBuffer(projectionCb, block);
    }

    private void uploadMaskState() => updateConstantBuffer(maskCb, maskState);

    private unsafe void updateConstantBuffer<T>(ID3D11Buffer buffer, in T data) where T : unmanaged
    {
        MappedSubresource mapped = context.Map(buffer, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        fixed (T* src = &data)
            Buffer.MemoryCopy(src, (void*)mapped.DataPointer, sizeof(T), sizeof(T));
        context.Unmap(buffer, 0);
    }

    public void SetRoot(DrawNode rootDrawNode) => rootNode = rootDrawNode;

    public void Resize(int physicalWidth, int physicalHeight, int logicalWidth, int logicalHeight)
    {
        if (device == null || swapChain == null)
            return;

        renderScaleX = (float)physicalWidth / logicalWidth;
        renderScaleY = (float)physicalHeight / logicalHeight;

        context.OMSetRenderTargets((ID3D11RenderTargetView)null);
        backBufferRtv?.Dispose();
        backBufferRtv = null;

        // Must reuse the creation flags (FrameLatencyWaitableObject / AllowTearing) — the waitable
        // handle stays valid across a resize, so no need to re-fetch it.
        swapChain.ResizeBuffers(0, (uint)Math.Max(1, physicalWidth), (uint)Math.Max(1, physicalHeight),
            Format.Unknown, swapChainFlags);

        createBackBufferView();

        // Mirror Metal: top=0/bottom=height, with the HLSL invertVertexOutputY handling the flip.
        projectionMatrix = Matrix4x4.CreateOrthographicOffCenter(0, logicalWidth, 0, logicalHeight, -1, 1);
    }

    public void Draw(IClock clock)
    {
        if (device == null || swapChain == null)
            return;

        stat_draw_calls.Value = 0;
        stat_vertices_drawn.Value = 0;
        stat_slot_exhaustion_flushes.Value = 0;
        stat_state_change_flushes.Value = 0;

        rootNode?.Draw(this);

        // Anything still pending belongs to this frame — issue it before the present.
        FlushBatch();

        // After the final flush, so the batch's binds land in this frame's count.
        TextureBindTracker.EndFrame();

        // VSync -> sync interval 1, no flags. Uncapped -> interval 0, tear (if the output supports it)
        // rather than stall on a flip-model swapchain. AllowTearing is illegal with interval > 0.
        if (presentSyncInterval == 0)
            swapChain.Present(0, allowTearing ? PresentFlags.AllowTearing : PresentFlags.None);
        else
            swapChain.Present(1, PresentFlags.None);
    }

    /// <summary>
    /// Sets the present sync interval from the framework frame limiter: <c>VSync</c> presents at the
    /// display rate (interval 1); every other mode presents uncapped (interval 0) and is paced by the
    /// draw clock, tearing if the output allows it. On a flip-model swapchain, mixing a non-VSync
    /// limiter with a VSync-locked present alternates buffers and visibly flashes — this mapping is
    /// what keeps the two in step. <c>SDLWindow.SetVSync</c> is a no-op for D3D11, so this is the single
    /// source of truth for the present interval.
    /// </summary>
    public void SetVSync(bool enabled) => presentSyncInterval = enabled ? 1 : 0;

    #region Draw path

    /// <summary>
    /// Resolves a texture to a batch slot index, binding it (and flushing on slot exhaustion) as
    /// required. Mirrors <c>GLRenderer.prepareTexture</c>, keyed on the shader resource view.
    /// </summary>
    private float prepareTexture(Texture texture)
    {
        var native = texture?.BackendTexture as D3D11Texture;

        // A texture with no D3D11 backing (e.g., a video texture, drawn by VideoDrawNode itself) or one
        // whose pixels haven't landed yet samples white. D3D11Texture.Bind applies that fallback
        // internally, so it has to be resolved here too, otherwise the slot would be keyed by a view
        // that is not what ends up bound to it.
        if (native == null || !native.Available)
            native = D3D11Texture.WhitePixel;

        if (native == null)
            return 0f;

        nint handle = native.Handle;

        for (int i = 0; i < boundTextureCount; i++)
        {
            if (boundTextureHandles[i] == handle)
                return i;
        }

        if (boundTextureCount < texture_slot_count)
        {
            int slot = boundTextureCount;
            // D3D11Texture.Bind counts the bind itself, so every backend's figure comes from the same place.
            native.Bind(slot);
            boundTextureHandles[slot] = handle;
            boundTextureCount++;
            return slot;
        }

        // All slots taken: flush and start a fresh slot set.
        stat_slot_exhaustion_flushes.Value++;
        FlushBatch();
        resetTextureSlots();

        native.Bind(0);
        boundTextureHandles[0] = handle;
        boundTextureCount = 1;
        return 0;
    }

    public void DrawVertices(ReadOnlySpan<SakuraVertex> vertices, Texture texture)
    {
        if (device == null || vertices.Length == 0)
            return;

        float textureIndex = prepareTexture(texture);

        // The clip state is injected per-vertex on the way into the batch (the pixel shader's
        // applyClipping reads it from the interpolators), exactly as GL's batch does.
        batch.AddRange(vertices, textureIndex, currentClip.ClipData, currentClip.ShearX, currentClip.Radius);
    }

    public void DrawQuads(ReadOnlySpan<SakuraVertex> vertices, Texture texture)
    {
        if (device == null)
            return;

        float textureIndex = prepareTexture(texture);

        for (int i = 0; i + 4 <= vertices.Length; i += 4)
            batch.AddQuad(vertices.Slice(i, 4), textureIndex, currentClip.ClipData, currentClip.ShearX, currentClip.Radius);
    }

    public void DrawVerticesRaw(ReadOnlySpan<SakuraVertex> vertices)
    {
        if (device == null || vertices.Length == 0)
            return;

        // This draws immediately, so anything still batched would end up *behind* it. Callers already
        // flush (VideoDrawNode, runShaderPass), but the ordering guarantee belongs here.
        FlushBatch();

        if (vertices.Length == 4)
        {
            Span<SakuraVertex> tri = stackalloc SakuraVertex[6];
            tri[0] = vertices[0];
            tri[1] = vertices[1];
            tri[2] = vertices[2];
            tri[3] = vertices[2];
            tri[4] = vertices[3];
            tri[5] = vertices[0];
            uploadAndDraw(tri);
        }
        else
        {
            uploadAndDraw(vertices);
        }

        // The caller bound its own textures (the effect source, or the three video planes) straight
        // onto the pixel stage, so the slot mirror no longer describes it.
        resetTextureSlots();
    }

    /// <summary>
    /// Uploads and draws the pending batch with a single <c>DrawIndexed</c>. Load-bearing: every state
    /// change the recorded geometry depends on (blend state, render target, mask constant buffer,
    /// shader) must call this first, or geometry recorded under one state is drawn under another.
    /// </summary>
    public unsafe void FlushBatch()
    {
        if (device == null || batch == null || batch.IsEmpty)
            return;

        int stride = SakuraVertex.Size;
        int vertexCount = batch.VertexCount;
        int indexCount = batch.IndexCount;

        MappedSubresource mappedVertices = context.Map(vertexBuffer, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        fixed (SakuraVertex* src = batch.Vertices)
            Buffer.MemoryCopy(src, (void*)mappedVertices.DataPointer, (long)vertexBufferCapacity * stride, (long)vertexCount * stride);
        context.Unmap(vertexBuffer, 0);

        if (batch.HasNonQuad)
        {
            MappedSubresource mappedIndices = context.Map(dynamicIndexBuffer, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
            fixed (uint* src = batch.Indices)
                Buffer.MemoryCopy(src, (void*)mappedIndices.DataPointer, (long)batch.MaxIndices * sizeof(uint), (long)indexCount * sizeof(uint));
            context.Unmap(dynamicIndexBuffer, 0);

            context.IASetIndexBuffer(dynamicIndexBuffer, Format.R32_UInt, 0);
        }
        else
        {
            // The static quad buffer already holds the exact pattern for indices [0, indexCount).
            context.IASetIndexBuffer(quadIndexBuffer, Format.R32_UInt, 0);
        }

        context.IASetVertexBuffer(0, vertexBuffer, (uint)stride, 0);
        context.DrawIndexed((uint)indexCount, 0, 0);

        stat_draw_calls.Value++;
        stat_vertices_drawn.Value += vertexCount;

        batch.Reset();
    }

    private unsafe void uploadAndDraw(ReadOnlySpan<SakuraVertex> vertices)
    {
        int stride = SakuraVertex.Size;
        ensureVertexCapacity(vertices.Length);

        MappedSubresource mapped = context.Map(vertexBuffer, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        fixed (SakuraVertex* src = vertices)
            Buffer.MemoryCopy(src, (void*)mapped.DataPointer, (long)vertexBufferCapacity * stride, (long)vertices.Length * stride);
        context.Unmap(vertexBuffer, 0);

        context.IASetVertexBuffer(0, vertexBuffer, (uint)stride, 0);
        context.Draw((uint)vertices.Length, 0);

        stat_draw_calls.Value++;
        stat_vertices_drawn.Value += vertices.Length;
    }

    /// <summary>
    /// Drops the CPU mirror of the pixel stage's shader-resource slots. The views themselves stay bound
    /// and are simply re-tracked (or replaced) on their next use.
    /// </summary>
    private void resetTextureSlots()
    {
        boundTextureCount = 0;
        Array.Clear(boundTextureHandles, 0, boundTextureHandles.Length);
    }

    /// <summary>
    /// Drops the slot mirror and records the white pixel as the slot-0 occupant, matching the
    /// <c>PSSetShaderResources(0, whiteSrvs)</c> that <see cref="rebindFrameState"/> has just issued.
    /// Solid-color drawables then cost no bind at all.
    /// </summary>
    private void resetTextureSlotsToWhite()
    {
        resetTextureSlots();

        if (D3D11Texture.WhitePixel == null)
            return;

        boundTextureHandles[0] = D3D11Texture.WhitePixel.Handle;
        boundTextureCount = 1;
    }

    private void ensureVertexCapacity(int vertexCount)
    {
        if (vertexBuffer != null && vertexBufferCapacity >= vertexCount)
            return;

        vertexBuffer?.Dispose();
        vertexBufferCapacity = Math.Max(vertexCount, Math.Max(256, vertexBufferCapacity * 2));
        vertexBuffer = device.CreateBuffer(new BufferDescription(
            (uint)(vertexBufferCapacity * SakuraVertex.Size), BindFlags.VertexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
    }

    public void SetBlendMode(BlendingMode blendingMode)
    {
        if (blendingMode == currentBlendMode)
            return;

        // The blend state applies at draw time, so the pending batch must go out under the old mode.
        stat_state_change_flushes.Value++;
        FlushBatch();

        currentBlendMode = blendingMode;
        context.OMSetBlendState(blendStates[(int)currentBlendMode]);
    }

    public void RestoreMainShader()
    {
        // A custom shader's SetUniformBlock rebinds its own CBs to VS b0 / PS b1, so restore the full
        // main-shader frame state (shader, CBs, sampler, textures) not just the projection content.
        currentShader = mainShader;
        maskState.IsBorder = 0;
        maskState.IsEdgeEffect = 0;
        rebindFrameState();
    }

    public void ApplyCurrentClip(Span<SakuraVertex> vertices)
    {
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i].ClipData = currentClip.ClipData;
            vertices[i].ClipShearX = currentClip.ShearX;
            vertices[i].ClipRadius = currentClip.Radius;
        }
    }

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

        var newCenter = new Vector2((left + right) / 2f, (top + bottom) / 2f);
        var newHalfSize = new Vector2((right - left) / 2f, (bottom - top) / 2f);

        // Collapsed intersection (child entirely outside parent) → shrink to ~zero so the shader
        // discards every fragment.
        if (left >= right || top >= bottom)
            newHalfSize = new Vector2(0.0001f, 0.0001f);
        else
            newHalfSize.X = Math.Max(0.0001f, newHalfSize.X - skewOffset);

        currentClip = new ClipState(new Vector4(newCenter.X, newCenter.Y, newHalfSize.X, newHalfSize.Y), shearX, cornerRadius);
    }

    public void PopMask(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius, float borderThickness, Color borderColor, ReadOnlySpan<SakuraVertex> maskVertices = default)
    {
        currentClip = clipStack.Count > 0 ? clipStack.Pop() : currentClip;
        drawBorder(maskCenter, maskHalfSize, shearX, cornerRadius, borderThickness, borderColor, maskVertices);
    }

    /// <summary>
    /// Draws the rounded/sheared border ring via the main shader's border path (u_IsBorder). Border
    /// geometry + color travel through the MaskBlock CB (PS b1)
    /// </summary>
    private void drawBorder(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius, float borderThickness, Color borderColor, ReadOnlySpan<SakuraVertex> vertices)
    {
        if (borderThickness <= 0 || vertices.Length < 4)
            return;

        // The MaskBlock upload below applies at draw time, so pending geometry has to be drawn under
        // the previous mask state first.
        FlushBatch();

        maskState.IsBorder = 1;
        maskState.MaskCenter = new Vector2(maskCenter.X, maskCenter.Y);
        maskState.MaskHalfSize = new Vector2(maskHalfSize.X, maskHalfSize.Y);
        maskState.ShearX = shearX;
        maskState.CornerRadius = cornerRadius;
        maskState.BorderThickness = borderThickness;
        maskState.BorderColor = new Vector4(borderColor.R / 255f, borderColor.G / 255f, borderColor.B / 255f, borderColor.A / 255f);
        uploadMaskState();

        DrawQuads(vertices[..4], WhitePixel);
        FlushBatch();

        maskState.IsBorder = 0;
        uploadMaskState();
    }

    /// <summary>
    /// Draws a soft edge effect (glow/shadow) via the main shader's edge-effect path (u_IsEdgeEffect)
    /// </summary>
    public void DrawEdgeEffect(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius, float edgeRadius, Vector2 offset, Color color, bool glow, bool hollow, ReadOnlySpan<SakuraVertex> quadVertices)
    {
        if (color.A == 0 || quadVertices.Length < 4)
            return;

        // As in drawBorder: the MaskBlock upload (and the glow blend swap) would otherwise apply
        // retroactively to pending geometry.
        FlushBatch();

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
        FlushBatch();

        maskState.IsEdgeEffect = 0;
        uploadMaskState();

        if (glow)
            SetBlendMode(previousBlend);
    }

    #endregion

    public void ScheduleToDrawThread(Action action) => drawThreadQueue.Enqueue(action);

    public void ScheduleTextureUpload(Action upload, long approximateBytes) => textureUploadQueue.Enqueue(upload, approximateBytes);

    public INativeTexture CreateNativeTexture(int width, int height) => new D3D11Texture(device, context, width, height);

    public IShader CreateShader(Storage storage, string vertexPath, string fragmentPath)
    {
        var (vert, frag) = ShaderCompiler.GetOrCompile(
            storage, vertexPath, fragmentPath, SPIRV.CrossCompileTarget.HLSL, ShaderCache);
        return new D3D11Shader(device, context, vert, frag, buildInputElements(), customShaderUniformBindings());
    }

    /// <summary>
    /// Block -> (stage, cbuffer register) map for the BufferedContainer effect shaders (blur/grayscale)
    /// and video. Same shape as the main shader (ProjectionBlock → VS b0; the single fragment block ->
    /// PS b1) — the sakura-spirv per-kind counter assigns them identically.
    /// </summary>
    private static IReadOnlyDictionary<string, D3D11Shader.UniformBinding> customShaderUniformBindings() =>
        new Dictionary<string, D3D11Shader.UniformBinding>
        {
            ["ProjectionBlock"] = new D3D11Shader.UniformBinding(D3D11Shader.Stage.Vertex, projection_cb_slot),
            ["BlurBlock"] = new D3D11Shader.UniformBinding(D3D11Shader.Stage.Fragment, mask_cb_slot),
            ["GrayscaleBlock"] = new D3D11Shader.UniformBinding(D3D11Shader.Stage.Fragment, mask_cb_slot),
            ["VideoBlock"] = new D3D11Shader.UniformBinding(D3D11Shader.Stage.Fragment, mask_cb_slot),
        };

    public INativeVideoTexture CreateVideoTexture(int width, int height) =>
        new D3D11VideoTexture(device, context, width, height);

    public IFrameBuffer CreateFrameBuffer(int width, int height, bool pixelSnapping = false) =>
        new D3D11FrameBuffer(device, context, width, height);

    /// <summary>
    /// Redirects rendering into <paramref name="frameBuffer"/>. Saves the current render target,
    /// viewport, projection and clip, remaps the projection so children render with their unchanged
    /// screen-space coordinates onto the buffer. The inverted top/bottom (vs the window projection)
    /// cancels the HLSL vertex shader's per-pass Y-flip so the GL-tuned BufferedContainer chain stays
    /// correct
    /// </summary>
    public void BindFrameBuffer(IFrameBuffer frameBuffer, RectangleF sourceRect, Color clearColor = default)
    {
        if (device == null || frameBuffer is not D3D11FrameBuffer fb)
            return;

        // Anything batched so far targets the previous render target — flush it there first.
        FlushBatch();

        frameBufferStack.Push(new FrameBufferState(currentRtv, currentViewportW, currentViewportH, projectionMatrix, currentClip));

        projectionMatrix = Matrix4x4.CreateOrthographicOffCenter(
            sourceRect.X, sourceRect.X + sourceRect.Width,
            sourceRect.Y + sourceRect.Height, sourceRect.Y,
            -1, 1);
        currentClip = ClipState.None;

        setRenderTarget(fb.RenderTargetView, fb.Width, fb.Height);
        context.ClearRenderTargetView(fb.RenderTargetView, toColor4(clearColor));
        uploadProjection();
    }

    public void UnbindFrameBuffer()
    {
        if (device == null || frameBufferStack.Count == 0)
            return;

        // Pending geometry belongs to the framebuffer — draw it before switching away.
        FlushBatch();

        var state = frameBufferStack.Pop();
        projectionMatrix = state.Projection;
        currentClip = state.Clip;
        setRenderTarget(state.Rtv, state.ViewportW, state.ViewportH);
        uploadProjection();
    }

    private static Color4 toColor4(Color c) => new Color4(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);

    public void Dispose()
    {
        mainShader?.Dispose();
        (WhitePixel?.BackendTexture as D3D11Texture)?.Dispose();

        foreach (var b in blendStates)
            b?.Dispose();

        rasterizerState?.Dispose();
        linearClampSampler?.Dispose();
        depthStencilOff?.Dispose();
        projectionCb?.Dispose();
        maskCb?.Dispose();
        vertexBuffer?.Dispose();
        quadIndexBuffer?.Dispose();
        dynamicIndexBuffer?.Dispose();

        if (instance == this)
            instance = null;

        backBufferRtv?.Dispose();
        waitableSwapChain?.Dispose();
        swapChain?.Dispose();
        context?.Dispose();
        device?.Dispose();
    }

    // The frame-latency waitable object is a Win32 event handle; there's no managed wrapper for it, so
    // wait on it via the kernel32 primitive. Alertable so the draw thread stays responsive to APCs.
    [DllImport("kernel32", SetLastError = true)]
    private static extern uint WaitForSingleObjectEx(nint handle, uint milliseconds, bool alertable);
}
