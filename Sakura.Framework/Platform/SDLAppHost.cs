// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using Sakura.Framework.Configurations;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Direct3D11;
using Sakura.Framework.Graphics.Rendering.Metal;
using Sakura.Framework.Graphics.Rendering.Vulkan;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Platform;

/// <summary>
/// A host whose window is an <see cref="SDLWindow"/>: it chooses the renderer for the platform, falls back
/// through the others when one fails to start, and sets the window up for the one it chose. Desktop and
/// mobile hosts build on this.
/// </summary>
public abstract class SDLAppHost : AppHost
{
    /// <summary>
    /// Tracks the renderer type that was successfully selected, so context init knows what to do.
    /// </summary>
    private RendererType selectedRendererType = RendererType.OpenGL;

    /// <summary>
    /// The renderers that failed to start this run, with why, in the order they failed. They aren't
    /// chosen again.
    /// </summary>
    private readonly List<(RendererType Type, Exception Failure)> failedRenderers = new List<(RendererType, Exception)>();

    protected SDLAppHost(string appName, HostOptions options = null) : base(appName, options)
    {
    }

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

            case RendererType.Vulkan:
                return VulkanRenderer.IsSupported() ? new VulkanRenderer() : null;

            default:
                return null;
        }
    }

    /// <summary>
    /// The renderer configured in <c>framework.ini</c>, if it isn't <see cref="RendererType.Automatic"/>,
    /// then <see cref="GetPreferredRenderers"/>, leaving out any that failed to start this run.
    /// </summary>
    private List<RendererType> getCandidates()
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

        candidates.RemoveAll(type => failedRenderers.Exists(failed => failed.Type == type));
        return candidates;
    }

    protected override IRenderer CreateRenderer()
    {
        foreach (var type in getCandidates())
        {
            var renderer = CreateRendererForType(type);

            if (renderer == null)
            {
                Logger.Verbose($"Renderer '{type}' isn't available here, skipping.");
                continue;
            }

            selectedRendererType = type;
            Logger.Verbose($"🖥️ Selected renderer: {type}");
            return renderer;
        }

        if (failedRenderers.Count > 0)
        {
            throw new InvalidOperationException($"No renderer could start: {string.Join(", ", failedRenderers.ConvertAll(f => f.Type))} failed, and no other is available.",
                new AggregateException(failedRenderers.ConvertAll(f => f.Failure)));
        }

        throw new InvalidOperationException("No suitable renderer could be initialised.");
    }

    /// <summary>
    /// Records the renderer that failed and goes on to the next. When none is left, the next
    /// <see cref="CreateRenderer"/> throws with every failure, rather than this one's alone escaping. The
    /// configured renderer stays configured: a driver problem may be fixed by the next run, and the log says
    /// what happened.
    /// </summary>
    protected override bool OnRendererFailed(Exception exception)
    {
        failedRenderers.Add((selectedRendererType, exception));

        Logger.Error(getCandidates().Count > 0
            ? $"Renderer '{selectedRendererType}' failed to start, so the next one is tried."
            : $"Renderer '{selectedRendererType}' failed to start, and no other is left to try.", exception);

        return true;
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
    /// Initialises the backend-specific context (GL context, or Metal/Win32/Vulkan surface) after
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

            case RendererType.Vulkan:
                sdlWindow.InitializeVulkanSurface();
                break;

            default:
                sdlWindow.InitializeGLContext();
                break;
        }
    }
}
