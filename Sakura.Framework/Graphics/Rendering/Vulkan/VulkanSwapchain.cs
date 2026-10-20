// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;
using Vortice.Vulkan;

namespace Sakura.Framework.Graphics.Rendering.Vulkan;

/// <summary>
/// The window's surface, the swapchain on it, and what each swapchain image needs to be rendered into
/// and presented: a view, a framebuffer, and a semaphore that presentation waits on.
/// </summary>
/// <remarks>
/// Everything here is rebuilt when the window resizes, the present mode changes, or the swapchain
/// reports itself out of date; the surface too when it is lost. The device and everything created on
/// it survive both.
/// </remarks>
internal sealed unsafe class VulkanSwapchain : IDisposable
{
    /// <summary>
    /// Three images whatever the present mode: latency is bounded by how many frames the renderer lets
    /// run ahead, not by the image count, and a third image keeps FIFO from stalling on a late frame.
    /// </summary>
    private const uint desired_image_count = 3;

    private readonly VulkanDevice device;
    private readonly IVulkanGraphicsSurface window;
    private readonly VulkanRenderPasses renderPasses;

    public VkSurfaceKHR Surface { get; private set; }
    public VkSwapchainKHR Handle { get; private set; }
    public VkFormat Format { get; private set; }
    public VkExtent2D Extent { get; private set; }
    public VkPresentModeKHR PresentMode { get; private set; }

    /// <summary>
    /// The pass every frame opens on this swapchain's images.
    /// </summary>
    public VkRenderPass RenderPass { get; private set; }

    /// <summary>
    /// Whether the swapchain must be rebuilt before the next image is acquired: never built yet, the
    /// window was resized or minimised, the present mode changed, or the last acquire or present
    /// reported it out of date.
    /// </summary>
    public bool NeedsRecreate { get; private set; } = true;

    private VkSurfaceFormatKHR surfaceFormat;
    private bool surfaceFormatChosen;
    private bool vsync = true;

    private VkImage[] images = [];
    private VkImageView[] views = [];
    private VkFramebuffer[] framebuffers = [];

    /// <summary>
    /// One per image, not per frame in flight. Presentation signals nothing when it is done with a
    /// semaphore, so the only proof that the present of image <c>i</c> no longer waits on its
    /// semaphore is that image <c>i</c> has been acquired again.
    /// </summary>
    private VkSemaphore[] renderFinished = [];

    private string? lastDescription;

    public VulkanSwapchain(VulkanDevice device, IVulkanGraphicsSurface window, VulkanRenderPasses renderPasses, VkSurfaceKHR surface)
    {
        this.device = device;
        this.window = window;
        this.renderPasses = renderPasses;
        Surface = surface;
    }

    /// <summary>
    /// Marks the swapchain for rebuilding before the next acquire.
    /// </summary>
    public void Invalidate() => NeedsRecreate = true;

    /// <summary>
    /// Selects FIFO (vsync) or an uncapped present mode, taking effect at the next rebuild.
    /// </summary>
    public void SetVSync(bool enabled)
    {
        if (vsync == enabled)
            return;

        vsync = enabled;
        NeedsRecreate = true;
    }

    /// <summary>
    /// Builds the swapchain for the window's current size, replacing the old one. Waits for the device
    /// to go idle first, since the old swapchain's images and semaphores are destroyed here; rebuilds
    /// happen on resizes and present-mode changes, not every frame.
    /// </summary>
    /// <returns>False when the window has no drawable area (minimised), in which case nothing is
    /// presented until a later call succeeds.</returns>
    public bool Recreate()
    {
        var api = device.InstanceApi;

        VkSurfaceCapabilitiesKHR capabilities;
        VulkanDevice.Check(api.vkGetPhysicalDeviceSurfaceCapabilitiesKHR(device.PhysicalDevice, Surface, &capabilities), "vkGetPhysicalDeviceSurfaceCapabilitiesKHR");

        var extent = chooseExtent(capabilities);

        if (extent.width == 0 || extent.height == 0)
        {
            NeedsRecreate = true;
            return false;
        }

        device.WaitIdle();

        if (!surfaceFormatChosen)
        {
            surfaceFormat = chooseSurfaceFormat();
            surfaceFormatChosen = true;
        }

        var presentMode = choosePresentMode();

        uint imageCount = Math.Max(desired_image_count, capabilities.minImageCount);

        if (capabilities.maxImageCount > 0)
            imageCount = Math.Min(imageCount, capabilities.maxImageCount);

        uint* families = stackalloc uint[] { device.GraphicsQueueFamily, device.PresentQueueFamily };
        bool shared = device.GraphicsQueueFamily != device.PresentQueueFamily;

        var oldSwapchain = Handle;

        var createInfo = new VkSwapchainCreateInfoKHR
        {
            surface = Surface,
            minImageCount = imageCount,
            imageFormat = surfaceFormat.format,
            imageColorSpace = surfaceFormat.colorSpace,
            imageExtent = extent,
            imageArrayLayers = 1,
            imageUsage = VkImageUsageFlags.ColorAttachment,
            // Drawing and presenting on different queue families shares the images between them rather
            // than transferring ownership back and forth every frame.
            imageSharingMode = shared ? VkSharingMode.Concurrent : VkSharingMode.Exclusive,
            queueFamilyIndexCount = shared ? 2u : 0u,
            pQueueFamilyIndices = shared ? families : null,
            // Desktop surfaces don't rotate. A rotated surface (Android) would need its current transform
            // here, with the window projection rotated to match.
            preTransform = (capabilities.supportedTransforms & VkSurfaceTransformFlagsKHR.Identity) != 0 ? VkSurfaceTransformFlagsKHR.Identity : capabilities.currentTransform,
            compositeAlpha = chooseCompositeAlpha(capabilities.supportedCompositeAlpha),
            presentMode = presentMode,
            clipped = true,
            oldSwapchain = oldSwapchain,
        };

        VkSwapchainKHR swapchain;
        VulkanDevice.Check(device.Api.vkCreateSwapchainKHR(&createInfo, null, &swapchain), "vkCreateSwapchainKHR");

        destroyImageResources();

        if (oldSwapchain.IsNotNull)
            device.Api.vkDestroySwapchainKHR(oldSwapchain, null);

        Handle = swapchain;
        Format = surfaceFormat.format;
        Extent = extent;
        PresentMode = presentMode;
        RenderPass = renderPasses.GetSwapchainPass(Format);

        createImageResources();

        NeedsRecreate = false;
        logIfChanged();
        return true;
    }

    /// <summary>
    /// Replaces a lost surface (<c>VK_ERROR_SURFACE_LOST_KHR</c>) with a new one on the same window. The
    /// swapchain is rebuilt by the next <see cref="Recreate"/>.
    /// </summary>
    public void RecreateSurface()
    {
        device.WaitIdle();
        destroySwapchain();
        window.DestroySurface(device.Instance.Handle, Surface);

        Surface = window.CreateSurface(device.Instance.Handle);

        VkBool32 supported;
        VulkanDevice.Check(device.InstanceApi.vkGetPhysicalDeviceSurfaceSupportKHR(device.PhysicalDevice, device.PresentQueueFamily, Surface, &supported), "vkGetPhysicalDeviceSurfaceSupportKHR");

        if (!supported)
            throw new InvalidOperationException("The recreated window surface can't be presented to from the device's present queue.");

        surfaceFormatChosen = false;
        NeedsRecreate = true;
    }

    /// <summary>
    /// Acquires the next image, signalling <paramref name="imageAvailable"/> when it can be rendered to.
    /// </summary>
    public VkResult Acquire(VkSemaphore imageAvailable, ulong timeoutNanoseconds, out uint imageIndex)
    {
        uint index;
        var result = device.Api.vkAcquireNextImageKHR(Handle, timeoutNanoseconds, imageAvailable, VkFence.Null, &index);
        imageIndex = index;
        return result;
    }

    /// <summary>
    /// Queues <paramref name="imageIndex"/> for presentation once its render-finished semaphore is
    /// signalled.
    /// </summary>
    public VkResult Present(uint imageIndex)
    {
        var swapchain = Handle;
        var wait = renderFinished[imageIndex];

        var presentInfo = new VkPresentInfoKHR
        {
            waitSemaphoreCount = 1,
            pWaitSemaphores = &wait,
            swapchainCount = 1,
            pSwapchains = &swapchain,
            pImageIndices = &imageIndex,
        };

        return device.Api.vkQueuePresentKHR(device.PresentQueue, &presentInfo);
    }

    public VkFramebuffer GetFramebuffer(uint imageIndex) => framebuffers[imageIndex];

    /// <summary>
    /// The semaphore the submission rendering into <paramref name="imageIndex"/> signals, and its
    /// present waits on.
    /// </summary>
    public VkSemaphore GetRenderFinished(uint imageIndex) => renderFinished[imageIndex];

    /// <summary>
    /// Requires the device to be idle.
    /// </summary>
    public void Dispose()
    {
        destroySwapchain();

        if (Surface.IsNotNull)
        {
            window.DestroySurface(device.Instance.Handle, Surface);
            Surface = VkSurfaceKHR.Null;
        }
    }

    private void destroySwapchain()
    {
        destroyImageResources();

        if (Handle.IsNotNull)
        {
            device.Api.vkDestroySwapchainKHR(Handle, null);
            Handle = VkSwapchainKHR.Null;
        }
    }

    private void createImageResources()
    {
        var api = device.Api;

        uint count;
        VulkanDevice.Check(api.vkGetSwapchainImagesKHR(Handle, &count, null), "vkGetSwapchainImagesKHR");

        images = new VkImage[count];

        fixed (VkImage* p = images)
            VulkanDevice.Check(api.vkGetSwapchainImagesKHR(Handle, &count, p), "vkGetSwapchainImagesKHR");

        views = new VkImageView[count];
        framebuffers = new VkFramebuffer[count];
        renderFinished = new VkSemaphore[count];

        for (int i = 0; i < count; i++)
        {
            var viewInfo = new VkImageViewCreateInfo
            {
                image = images[i],
                viewType = VkImageViewType.Image2D,
                format = Format,
                components = new VkComponentMapping
                {
                    r = VkComponentSwizzle.Identity,
                    g = VkComponentSwizzle.Identity,
                    b = VkComponentSwizzle.Identity,
                    a = VkComponentSwizzle.Identity,
                },
                subresourceRange = new VkImageSubresourceRange
                {
                    aspectMask = VkImageAspectFlags.Color,
                    baseMipLevel = 0,
                    levelCount = 1,
                    baseArrayLayer = 0,
                    layerCount = 1,
                },
            };

            VkImageView view;
            VulkanDevice.Check(api.vkCreateImageView(&viewInfo, null, &view), "vkCreateImageView");
            views[i] = view;

            var framebufferInfo = new VkFramebufferCreateInfo
            {
                renderPass = RenderPass,
                attachmentCount = 1,
                pAttachments = &view,
                width = Extent.width,
                height = Extent.height,
                layers = 1,
            };

            VkFramebuffer framebuffer;
            VulkanDevice.Check(api.vkCreateFramebuffer(&framebufferInfo, null, &framebuffer), "vkCreateFramebuffer");
            framebuffers[i] = framebuffer;

            var semaphoreInfo = new VkSemaphoreCreateInfo();
            VkSemaphore semaphore;
            VulkanDevice.Check(api.vkCreateSemaphore(&semaphoreInfo, null, &semaphore), "vkCreateSemaphore");
            renderFinished[i] = semaphore;

            device.SetName(VkObjectType.Image, images[i].Handle, $"Swapchain image {i}");
            device.SetName(VkObjectType.Semaphore, semaphore.Handle, $"Swapchain image {i} render finished");
        }
    }

    private void destroyImageResources()
    {
        var api = device.Api;

        foreach (var framebuffer in framebuffers)
            api.vkDestroyFramebuffer(framebuffer, null);

        foreach (var view in views)
            api.vkDestroyImageView(view, null);

        foreach (var semaphore in renderFinished)
            api.vkDestroySemaphore(semaphore, null);

        // The images belong to the swapchain and go with it.
        images = [];
        views = [];
        framebuffers = [];
        renderFinished = [];
    }

    private VkExtent2D chooseExtent(VkSurfaceCapabilitiesKHR capabilities)
    {
        // 0xFFFFFFFF means the surface takes its size from the swapchain (Wayland), so the window's
        // drawable size decides.
        if (capabilities.currentExtent.width != uint.MaxValue)
            return capabilities.currentExtent;

        window.GetDrawableSize(out int width, out int height);

        return new VkExtent2D
        {
            width = Math.Clamp((uint)Math.Max(0, width), capabilities.minImageExtent.width, capabilities.maxImageExtent.width),
            height = Math.Clamp((uint)Math.Max(0, height), capabilities.minImageExtent.height, capabilities.maxImageExtent.height),
        };
    }

    private VkSurfaceFormatKHR chooseSurfaceFormat()
    {
        var api = device.InstanceApi;

        uint count;
        VulkanDevice.Check(api.vkGetPhysicalDeviceSurfaceFormatsKHR(device.PhysicalDevice, Surface, &count, null), "vkGetPhysicalDeviceSurfaceFormatsKHR");

        var formats = stackalloc VkSurfaceFormatKHR[(int)count];
        VulkanDevice.Check(api.vkGetPhysicalDeviceSurfaceFormatsKHR(device.PhysicalDevice, Surface, &count, formats), "vkGetPhysicalDeviceSurfaceFormatsKHR");

        // Shaders write linear color and rely on the attachment to encode it, as the sRGB back buffers
        // of the other backends do. BGRA is what desktops usually offer, RGBA what Android does.
        foreach (var wanted in (ReadOnlySpan<VkFormat>)[VkFormat.B8G8R8A8Srgb, VkFormat.R8G8B8A8Srgb])
        {
            for (int i = 0; i < count; i++)
            {
                if (formats[i].format == wanted && formats[i].colorSpace == VkColorSpaceKHR.SrgbNonLinear)
                    return formats[i];
            }
        }

        foreach (var fallback in (ReadOnlySpan<VkFormat>)[VkFormat.B8G8R8A8Unorm, VkFormat.R8G8B8A8Unorm])
        {
            for (int i = 0; i < count; i++)
            {
                if (formats[i].format == fallback && formats[i].colorSpace == VkColorSpaceKHR.SrgbNonLinear)
                {
                    Logger.Warning($"Vulkan: the window offers no sRGB format, so {fallback} is used. Colors will be darker than on other renderers.");
                    return formats[i];
                }
            }
        }

        if (count == 0)
            throw new InvalidOperationException("The window surface reports no formats.");

        Logger.Warning($"Vulkan: the window offers neither sRGB nor 8-bit RGBA formats; using {formats[0].format} ({formats[0].colorSpace}). Colors will be wrong.");
        return formats[0];
    }

    private VkPresentModeKHR choosePresentMode()
    {
        // FIFO is the only mode every device has, and the only one that waits for vertical blank.
        if (vsync)
            return VkPresentModeKHR.Fifo;

        var api = device.InstanceApi;

        uint count;
        VulkanDevice.Check(api.vkGetPhysicalDeviceSurfacePresentModesKHR(device.PhysicalDevice, Surface, &count, null), "vkGetPhysicalDeviceSurfacePresentModesKHR");

        var modes = stackalloc VkPresentModeKHR[(int)count];
        VulkanDevice.Check(api.vkGetPhysicalDeviceSurfacePresentModesKHR(device.PhysicalDevice, Surface, &count, modes), "vkGetPhysicalDeviceSurfacePresentModesKHR");

        // Uncapped: tear rather than wait (IMMEDIATE), as the other renderers do, or at least never block
        // on the display (MAILBOX).
        foreach (var wanted in (ReadOnlySpan<VkPresentModeKHR>)[VkPresentModeKHR.Immediate, VkPresentModeKHR.Mailbox])
        {
            for (int i = 0; i < count; i++)
            {
                if (modes[i] == wanted)
                    return wanted;
            }
        }

        return VkPresentModeKHR.Fifo;
    }

    private static VkCompositeAlphaFlagsKHR chooseCompositeAlpha(VkCompositeAlphaFlagsKHR supported)
    {
        foreach (var wanted in (ReadOnlySpan<VkCompositeAlphaFlagsKHR>)[VkCompositeAlphaFlagsKHR.Opaque, VkCompositeAlphaFlagsKHR.Inherit, VkCompositeAlphaFlagsKHR.PreMultiplied, VkCompositeAlphaFlagsKHR.PostMultiplied])
        {
            if ((supported & wanted) != 0)
                return wanted;
        }

        return VkCompositeAlphaFlagsKHR.Opaque;
    }

    private void logIfChanged()
    {
        // The size is left out: it changes on every resize, and the rest rarely does.
        string description = $"{Format} {surfaceFormat.colorSpace}, {PresentMode}{(vsync ? string.Empty : PresentMode == VkPresentModeKHR.Fifo ? " (uncapped requested, but FIFO is all the window offers)" : string.Empty)}, {images.Length} images";

        if (description == lastDescription)
            return;

        lastDescription = description;
        Logger.Verbose($"Vulkan swapchain: {description}, {Extent.width}x{Extent.height}");
    }
}
