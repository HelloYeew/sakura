// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Concurrent;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.IO;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
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
    private ID3D11Device device;
    private ID3D11DeviceContext context;

    private IDXGISwapChain1 swapChain;
    private ID3D11RenderTargetView backBufferRtv;
    private int backBufferWidth;
    private int backBufferHeight;

    private nint windowHandle;

    private readonly ConcurrentQueue<Action> drawThreadQueue = new();

    private DrawNode rootNode;
    private Matrix4x4 projectionMatrix = Matrix4x4.Identity;

    private float renderScaleX = 1.0f;
    private float renderScaleY = 1.0f;

    private static readonly Color4 clear_colour = new Color4(0f, 0f, 0f, 1f);

    public Texture WhitePixel { get; private set; }

    public Storage ShaderStorage { get; set; }

    public DiskCache ShaderCache { get; set; }

    public Matrix4x4 ProjectionMatrix => projectionMatrix;

    public Vector2 RenderScale => new Vector2(renderScaleX, renderScaleY);

    public void Initialize(IGraphicsSurface graphicsSurface)
    {
        if (graphicsSurface is not IWin32GraphicsSurface win32Surface)
            throw new InvalidOperationException($"{nameof(D3D11Renderer)} requires an {nameof(IWin32GraphicsSurface)}.");

        windowHandle = win32Surface.WindowHandle;

        // BGRA support is required for the DXGI flip-model swapchain and Direct2D/DWrite interop.
        var flags = DeviceCreationFlags.BgraSupport;
#if DEBUG
        // The debug layer needs the Windows "Graphics Tools" optional feature installed. If device
        // creation fails with the flag on a bare VM, drop it (handled by the retry below).
        flags |= DeviceCreationFlags.Debug;
#endif

        FeatureLevel[] featureLevels =
        {
            FeatureLevel.Level_11_1,
            FeatureLevel.Level_11_0,
        };

        if (!tryCreateDevice(flags, featureLevels))
        {
#if DEBUG
            Logger.Verbose("D3D11 device creation with the debug layer failed; retrying without it. " +
                           "Install the Windows 'Graphics Tools' feature for validation output.");
            if (!tryCreateDevice(flags & ~DeviceCreationFlags.Debug, featureLevels))
#endif
                throw new InvalidOperationException("Failed to create the Direct3D 11 device.");
        }

        logDeviceInfo();
        createSwapChain();
    }

    private bool tryCreateDevice(DeviceCreationFlags flags, FeatureLevel[] featureLevels)
    {
        var result = D3D11CreateDevice(
            null,
            DriverType.Hardware,
            flags,
            featureLevels,
            out device,
            out context
        );

        return result.Success && device != null;
    }

    /// <summary>
    /// Logs the active adapter and feature level at startup, mirroring the GL/Metal backends'
    /// device diagnostics.
    /// </summary>
    private void logDeviceInfo()
    {
        try
        {
            using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
            using IDXGIAdapter adapter = dxgiDevice.GetAdapter();
            AdapterDescription desc = adapter.Description;

            Logger.Verbose($"🖥️ Direct3D11 adapter: {desc.Description?.Trim()} " +
                           $"(feature level {device.FeatureLevel})");
        }
        catch (Exception e)
        {
            Logger.Verbose($"Direct3D11 device created; adapter info unavailable ({e.Message}).");
        }
    }

    /// <summary>
    /// Creates the DXGI flip-model swapchain on the window's HWND and its back-buffer RTV.
    /// Width/height are left at 0 so DXGI sizes to the window client area; <see cref="Resize"/>
    /// re-sizes via <c>ResizeBuffers</c> thereafter.
    /// </summary>
    private void createSwapChain()
    {
        using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
        using IDXGIAdapter adapter = dxgiDevice.GetAdapter();
        using IDXGIFactory2 factory = adapter.GetParent<IDXGIFactory2>();

        var desc = new SwapChainDescription1
        {
            Width = 0,
            Height = 0,
            // Non-sRGB buffer format (required for flip-model)
            Format = Format.B8G8R8A8_UNorm,
            Stereo = false,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = 2,
            Scaling = Scaling.None,
            SwapEffect = SwapEffect.FlipDiscard,
            AlphaMode = AlphaMode.Ignore,
            // TODO: will add low-latency support
            Flags = SwapChainFlags.None,
        };

        swapChain = factory.CreateSwapChainForHwnd(device, windowHandle, desc);

        // disable DXGI's Alt-Enter fullscreen toggle (already handle in window)
        factory.MakeWindowAssociation(windowHandle, WindowAssociationFlags.IgnoreAltEnter);

        createBackBufferView();
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

    public void Clear()
    {
        // Folded into StartFrame (the RTV is cleared there once it's bound).
    }

    public void StartFrame()
    {
        if (device == null || swapChain == null)
            return;

        // Drain queued uploads (textures, glyphs) on the draw thread before the frame's draws.
        while (drawThreadQueue.TryDequeue(out var action))
            action();

        context.OMSetRenderTargets(backBufferRtv);
        context.RSSetViewport(0, 0, backBufferWidth, backBufferHeight);
        context.ClearRenderTargetView(backBufferRtv, clear_colour);
    }

    public void SetRoot(DrawNode rootDrawNode) => rootNode = rootDrawNode;

    public void Resize(int physicalWidth, int physicalHeight, int logicalWidth, int logicalHeight)
    {
        if (device == null || swapChain == null)
            return;

        renderScaleX = (float)physicalWidth / logicalWidth;
        renderScaleY = (float)physicalHeight / logicalHeight;

        // Release the RTV (its reference to the back buffer must be gone before ResizeBuffers).
        context.OMSetRenderTargets((ID3D11RenderTargetView)null);
        backBufferRtv?.Dispose();
        backBufferRtv = null;

        swapChain.ResizeBuffers(0, (uint)Math.Max(1, physicalWidth), (uint)Math.Max(1, physicalHeight),
            Format.Unknown, SwapChainFlags.None);

        createBackBufferView();

        // Mirror Metal: top=0, bottom=height, with the HLSL vertex shader's Y-flip
        // (invertVertexOutputY) landing the top-left origin correctly.
        projectionMatrix = Matrix4x4.CreateOrthographicOffCenter(0, logicalWidth, 0, logicalHeight, -1, 1);
    }

    public void Draw(IClock clock)
    {
        if (device == null || swapChain == null)
            return;

        rootNode?.Draw(this);

        // Present with VSync on (sync interval 1)
        // TODO: Frame-limiter usage later
        swapChain.Present(1, PresentFlags.None);
    }

    public void DrawVertices(ReadOnlySpan<SakuraVertex> vertices, Texture textureGl)
    {
    }

    public void DrawQuads(ReadOnlySpan<SakuraVertex> vertices, Texture textureGl)
    {
    }

    public void DrawVerticesRaw(ReadOnlySpan<SakuraVertex> vertices)
    {
    }

    public void PushMask(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius)
    {
    }

    public void PopMask(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius, float borderThickness, Color borderColor, ReadOnlySpan<SakuraVertex> maskVertices = default)
    {
    }

    public void DrawEdgeEffect(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius, float edgeRadius, Vector2 offset, Color color, bool glow, bool hollow, ReadOnlySpan<SakuraVertex> quadVertices)
    {
    }

    public void SetBlendMode(BlendingMode blendingMode)
    {
    }

    public void FlushBatch()
    {
    }

    public void RestoreMainShader()
    {
    }

    public void ScheduleToDrawThread(Action action) => drawThreadQueue.Enqueue(action);

    public IShader CreateShader(Storage storage, string vertexPath, string fragmentPath) =>
        throw new NotImplementedException();

    public INativeVideoTexture CreateVideoTexture(int width, int height) =>
        throw new NotImplementedException();

    public INativeTexture CreateNativeTexture(int width, int height) =>
        throw new NotImplementedException();

    public IFrameBuffer CreateFrameBuffer(int width, int height, bool pixelSnapping = false) =>
        new D3D11FrameBuffer(width, height);

    public void BindFrameBuffer(IFrameBuffer frameBuffer, RectangleF sourceRect, Color clearColour = default)
    {
    }

    public void UnbindFrameBuffer()
    {
    }

    public void Dispose()
    {
        WhitePixel?.Dispose();
        backBufferRtv?.Dispose();
        swapChain?.Dispose();
        context?.Dispose();
        device?.Dispose();
    }
}
