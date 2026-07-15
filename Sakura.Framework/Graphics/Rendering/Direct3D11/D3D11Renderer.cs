// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Concurrent;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.IO;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Timing;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;
using SakuraVertex = Sakura.Framework.Graphics.Rendering.Vertex.Vertex;

namespace Sakura.Framework.Graphics.Rendering.Direct3D11;

/// <summary>
/// Direct3D 11 renderer backend (managed, via Vortice.Windows).
/// </summary>
public sealed class D3D11Renderer : ID3D11Renderer, IDisposable
{
    private ID3D11Device device;
    private ID3D11DeviceContext context;

    private nint windowHandle;

    private readonly ConcurrentQueue<Action> drawThreadQueue = new();

    private Matrix4x4 projectionMatrix = Matrix4x4.Identity;

    public Texture WhitePixel { get; private set; }

    public Storage ShaderStorage { get; set; }

    public DiskCache ShaderCache { get; set; }

    public Matrix4x4 ProjectionMatrix => projectionMatrix;

    public Vector2 RenderScale => Vector2.One;

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
    /// Logs the active adapter and feature level at startup
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

    public void Clear()
    {
    }

    public void StartFrame()
    {
        // Drain draw-thread work now so scheduled resource creation runs on the draw thread,
        // matching the other backends
        while (drawThreadQueue.TryDequeue(out var action))
            action();
    }

    public void SetRoot(DrawNode rootDrawNode)
    {
    }

    public void Resize(int physicalWidth, int physicalHeight, int logicalWidth, int logicalHeight)
    {
    }

    public void Draw(IClock clock)
    {
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
        throw new NotImplementedException();

    public void BindFrameBuffer(IFrameBuffer frameBuffer, RectangleF sourceRect, Color clearColour = default) =>
        throw new NotImplementedException();

    public void UnbindFrameBuffer() =>
        throw new NotImplementedException();

    public void Dispose()
    {
        WhitePixel?.Dispose();
        context?.Dispose();
        device?.Dispose();
    }
}
