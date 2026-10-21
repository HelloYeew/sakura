// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;

namespace Sakura.Framework.Platform;

/// <summary>
/// Extended surface contract for Vulkan rendering.
/// </summary>
/// <remarks>
/// Unlike the other surfaces, the window can't hand over a ready-made native handle: a
/// <c>VkSurfaceKHR</c> belongs to a <c>VkInstance</c>, and only the renderer creates that. So the
/// renderer reads <see cref="RequiredInstanceExtensions"/>, creates its instance with them, and then
/// asks the window to create the surface on it. Handles cross this interface as raw values so the
/// platform layer doesn't depend on a binding library.
/// </remarks>
public interface IVulkanGraphicsSurface : IGraphicsSurface
{
    /// <summary>
    /// The instance extensions the window system needs to present: <c>VK_KHR_surface</c> plus the
    /// platform's own (<c>VK_KHR_win32_surface</c>, <c>VK_KHR_xlib_surface</c>,
    /// <c>VK_KHR_wayland_surface</c>, <c>VK_EXT_metal_surface</c>, ...).
    /// </summary>
    IReadOnlyList<string> RequiredInstanceExtensions { get; }

    /// <summary>
    /// Creates a <c>VkSurfaceKHR</c> for this window on <paramref name="instance"/> (a <c>VkInstance</c>)
    /// and returns it. Throws if the window system refuses.
    /// </summary>
    /// <remarks>
    /// Can be called again after <see cref="DestroySurface"/>: a surface can be lost while the window
    /// lives on (<c>VK_ERROR_SURFACE_LOST_KHR</c>, or an Android app going to the background), and the
    /// renderer then rebuilds only the surface and its swapchain.
    /// </remarks>
    ulong CreateSurface(nint instance);

    /// <summary>
    /// <see cref="CreateSurface"/> for when the window may have no surface to give right now (an Android app in
    /// the background): false then, rather than an error.
    /// </summary>
    bool TryCreateSurface(nint instance, out ulong surface);

    /// <summary>
    /// Destroys a surface returned by <see cref="CreateSurface"/>. The caller must have destroyed every
    /// swapchain created on it first.
    /// </summary>
    void DestroySurface(nint instance, ulong surface);

    /// <summary>
    /// The window's drawable size in physical pixels. The swapchain extent comes from this when the
    /// surface leaves it to the application (Wayland reports its current extent as 0xFFFFFFFF).
    /// </summary>
    void GetDrawableSize(out int width, out int height);
}
