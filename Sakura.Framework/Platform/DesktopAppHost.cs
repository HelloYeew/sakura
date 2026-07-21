// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using JetBrains.Annotations;
using Sakura.Framework.Configurations;
using Sakura.Framework.Extensions;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Direct3D11;
using Sakura.Framework.Graphics.Rendering.Metal;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Platform;

public class DesktopAppHost : AppHost
{
    public bool IsPortableInstallation { get; }

    /// <summary>
    /// Tracks the renderer type that was successfully selected, so context init knows what to do.
    /// </summary>
    private RendererType selectedRendererType = RendererType.OpenGL;

    public DesktopAppHost(string appName, HostOptions options = null) : base(appName, options)
    {
        IsPortableInstallation = Options.PortableInstallation;
    }

    protected sealed override Storage GetDefaultAppStorage()
    {
        if (IsPortableInstallation || File.Exists(Path.Combine(RuntimeInfo.StartupDirectory, "framework.ini")))
            return GetStorage(RuntimeInfo.StartupDirectory);

        return base.GetDefaultAppStorage();
    }

    public override Storage GetStorage(string path) => new DesktopStorage(path, this);

    protected override IWindow CreateWindow() => new DesktopWindow();

    /// <summary>
    /// Returns the preferred renderer types in priority order for this platform.
    /// Override to change backend preference.
    /// </summary>
    protected virtual IEnumerable<RendererType> GetPreferredRenderers() => RendererTypes.GetPlatformRenderers();

    /// <summary>
    /// Creates a renderer instance for the given type. Return null to skip that type.
    /// Override to register additional renderer backends.
    /// </summary>
    [CanBeNull]
    protected virtual IRenderer CreateRendererForType(RendererType type)
    {
        switch (type)
        {
            case RendererType.OpenGL:
                return new GLRenderer();

            case RendererType.Metal:
                return RuntimeInfo.IsMacOS ? new MetalRenderer() : null;

            case RendererType.Direct3D11:
                return RuntimeInfo.IsWindows && D3D11Renderer.IsSupported() ? new D3D11Renderer() : null;

            default:
                return null;
        }
    }

    protected override IRenderer CreateRenderer()
    {
        var configured = FrameworkConfigManager.Get<RendererType>(FrameworkSetting.RendererType).Value;

        var candidates = new List<RendererType>();

        if (configured != RendererType.Automatic)
            candidates.Add(configured);

        foreach (var preferred in GetPreferredRenderers())
        {
            if (!candidates.Contains(preferred))
                candidates.Add(preferred);
        }

        foreach (var type in candidates)
        {
            var renderer = CreateRendererForType(type);

            if (renderer == null)
            {
                Logger.Verbose($"Renderer '{type}' is not available on this platform, skipping.");
                continue;
            }

            selectedRendererType = type;
            Logger.Verbose($"🖥️ Selected renderer: {type}");
            return renderer;
        }

        throw new InvalidOperationException("No suitable renderer could be initialised.");
    }

    /// <summary>
    /// Calls <see cref="SDLWindow.SetGraphicsApi"/> so the window creates with the correct flags
    /// before <see cref="IWindow.Create"/> runs.
    /// </summary>
    protected override void PrepareWindowForRenderer(IWindow window)
    {
        if (window is SDLWindow sdlWindow)
        {
            sdlWindow.SetGraphicsApi(selectedRendererType);
            sdlWindow.SetWindowConfig(FrameworkConfigManager);
        }
    }

    /// <summary>
    /// Initialises the backend-specific context (GL context or Metal surface) after
    /// the SDL window exists but before the renderer's <c>Initialize</c> call.
    /// </summary>
    protected override void InitializeGraphicsContext(IWindow window, IRenderer renderer)
    {
        if (window is not SDLWindow sdlWindow) return;

        switch (selectedRendererType)
        {
            case RendererType.Metal:
                sdlWindow.InitializeMetalSurface();
                break;

            case RendererType.Direct3D11:
                sdlWindow.InitializeWin32Surface();
                break;

            default:
                sdlWindow.InitializeGLContext();
                break;
        }
    }

    public override bool OpenFileExternally(string filename)
    {
        openUsingShellExecute(filename);
        return true;
    }

    public override bool PresentFileExternally(string filename)
    {
        OpenFileExternally(Path.GetDirectoryName(filename.TrimDirectorySeparator()));
        return true;
    }

    public override void OpenUrlExternally(string url)
    {
        if (!url.CheckIsValidUrl())
            throw new ArgumentException("The provided URL is not a valid protocol to open externally, it must start with http://, https:// or mailto:.", nameof(url));

        try
        {
            openUsingShellExecute(url);
        }
        catch (Exception ex)
        {
            Logger.Error("Unable to open external link.", ex);
        }
    }

    private static void openUsingShellExecute(string path) => Process.Start(new ProcessStartInfo
    {
        FileName = path,
        UseShellExecute = true
    });

    protected override void Dispose(bool isDisposing)
    {
        base.Dispose(isDisposing);
    }
}
