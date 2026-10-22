// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;

namespace Sakura.Framework.Platform;

/// <summary>
/// <see cref="IVulkanGraphicsSurface"/> implementation backed by an SDL3 window.
/// <see cref="RequiredInstanceExtensions"/> is set by <see cref="SDLWindow.InitializeVulkanSurface"/>.
/// </summary>
public class VulkanGraphicsSurface : IVulkanGraphicsSurface
{
    private readonly SDLWindow window;

    internal VulkanGraphicsSurface(SDLWindow window)
    {
        this.window = window;
    }

    public GraphicsSurfaceType Type => GraphicsSurfaceType.Vulkan;

    public IReadOnlyList<string> RequiredInstanceExtensions { get; internal set; } = Array.Empty<string>();

    public ulong CreateSurface(nint instance) => window.CreateVulkanSurface(instance);

    public bool TryCreateSurface(nint instance, out ulong surface) => window.TryCreateVulkanSurface(instance, out surface);

    public void DestroySurface(nint instance, ulong surface) => window.DestroyVulkanSurface(instance, surface);

    public void GetDrawableSize(out int width, out int height) => window.GetDrawableSize(out width, out height);
}
