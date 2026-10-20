// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Vortice.Vulkan;

namespace Sakura.Framework.Graphics.Rendering.Vulkan;

internal enum VulkanPassKind
{
    /// <summary>
    /// Opens a frame on a swapchain image: clears it, and leaves it ready to present.
    /// </summary>
    SwapchainClear,

    /// <summary>
    /// Reopens the swapchain image after an offscreen pass, keeping what was drawn before.
    /// </summary>
    SwapchainLoad,

    /// <summary>
    /// Opens an offscreen framebuffer: clears it, and leaves it ready to be sampled.
    /// </summary>
    OffscreenClear,

    /// <summary>
    /// Reopens an offscreen framebuffer after a nested one, keeping what was drawn before.
    /// </summary>
    OffscreenLoad,
}

/// <summary>
/// Render passes by attachment format and kind, created on first use and kept for the device's
/// lifetime.
/// </summary>
/// <remarks>
/// <para>
/// Every pass has one color attachment and nothing else: masking is a signed-distance test in the
/// fragment shader, so there is no depth or stencil to allocate, clear or store, which is the cheapest
/// case a tile-based GPU can get. The passes perform their own layout transitions, so no barrier is
/// recorded around them.
/// </para>
/// <para>
/// All four kinds of a format are <em>compatible</em>: they differ only in load operations and
/// layouts, which is everything compatibility allows to differ. Their dependencies are identical on
/// purpose, so one pipeline per shader and blend mode can draw into any of them.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanRenderPasses : IDisposable
{
    private readonly VulkanDevice device;
    private readonly Dictionary<(VkFormat Format, VulkanPassKind Kind), VkRenderPass> passes = new Dictionary<(VkFormat, VulkanPassKind), VkRenderPass>();

    public VulkanRenderPasses(VulkanDevice device)
    {
        this.device = device;
    }

    public VkRenderPass Get(VkFormat format, VulkanPassKind kind)
    {
        if (passes.TryGetValue((format, kind), out var pass))
            return pass;

        bool load = kind is VulkanPassKind.SwapchainLoad or VulkanPassKind.OffscreenLoad;
        bool swapchain = kind is VulkanPassKind.SwapchainClear or VulkanPassKind.SwapchainLoad;
        var finalLayout = swapchain ? VkImageLayout.PresentSrcKHR : VkImageLayout.ShaderReadOnlyOptimal;

        var attachment = new VkAttachmentDescription
        {
            format = format,
            samples = VkSampleCountFlags.Count1,
            loadOp = load ? VkAttachmentLoadOp.Load : VkAttachmentLoadOp.Clear,
            storeOp = VkAttachmentStoreOp.Store,
            stencilLoadOp = VkAttachmentLoadOp.DontCare,
            stencilStoreOp = VkAttachmentStoreOp.DontCare,
            // A cleared image's previous contents don't matter; a reopened one is wherever the pass that
            // closed it left it.
            initialLayout = load ? finalLayout : VkImageLayout.Undefined,
            finalLayout = finalLayout,
        };

        var colorReference = new VkAttachmentReference
        {
            attachment = 0,
            layout = VkImageLayout.ColorAttachmentOptimal,
        };

        var subpass = new VkSubpassDescription
        {
            pipelineBindPoint = VkPipelineBindPoint.Graphics,
            colorAttachmentCount = 1,
            pColorAttachments = &colorReference,
        };

        var dependencies = stackalloc VkSubpassDependency[2];

        // Into the pass: the attachment's layout transition and first write wait for
        //  - the swapchain image's acquire semaphore, waited on at color-attachment output;
        //  - earlier passes that wrote this image (a load reads what they wrote);
        //  - earlier fragment shaders that sampled it, in this frame or one still in flight.
        dependencies[0] = new VkSubpassDependency
        {
            srcSubpass = Vortice.Vulkan.Vulkan.VK_SUBPASS_EXTERNAL,
            dstSubpass = 0,
            srcStageMask = VkPipelineStageFlags.ColorAttachmentOutput | VkPipelineStageFlags.FragmentShader,
            dstStageMask = VkPipelineStageFlags.ColorAttachmentOutput,
            srcAccessMask = VkAccessFlags.ColorAttachmentWrite,
            dstAccessMask = VkAccessFlags.ColorAttachmentRead | VkAccessFlags.ColorAttachmentWrite,
        };

        // Out of the pass: what was drawn is visible to fragment shaders that sample it afterwards
        // (an offscreen buffer composited into its parent).
        dependencies[1] = new VkSubpassDependency
        {
            srcSubpass = 0,
            dstSubpass = Vortice.Vulkan.Vulkan.VK_SUBPASS_EXTERNAL,
            srcStageMask = VkPipelineStageFlags.ColorAttachmentOutput,
            dstStageMask = VkPipelineStageFlags.FragmentShader,
            srcAccessMask = VkAccessFlags.ColorAttachmentWrite,
            dstAccessMask = VkAccessFlags.ShaderRead,
        };

        var createInfo = new VkRenderPassCreateInfo
        {
            attachmentCount = 1,
            pAttachments = &attachment,
            subpassCount = 1,
            pSubpasses = &subpass,
            dependencyCount = 2,
            pDependencies = dependencies,
        };

        VulkanDevice.Check(device.Api.vkCreateRenderPass(&createInfo, null, &pass), "vkCreateRenderPass");
        device.SetName(VkObjectType.RenderPass, pass.Handle, $"{kind} ({format})");

        passes[(format, kind)] = pass;
        return pass;
    }

    /// <summary>
    /// Assumes the device is idle.
    /// </summary>
    public void Dispose()
    {
        foreach (var pass in passes.Values)
            device.Api.vkDestroyRenderPass(pass, null);

        passes.Clear();
    }
}
