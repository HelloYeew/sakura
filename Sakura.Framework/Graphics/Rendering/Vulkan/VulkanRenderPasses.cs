// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Vortice.Vulkan;

namespace Sakura.Framework.Graphics.Rendering.Vulkan;

/// <summary>
/// Render passes by attachment format, created on first use and kept for the device's lifetime.
/// </summary>
/// <remarks>
/// Every pass has one color attachment and nothing else: masking is a signed-distance test in the
/// fragment shader, so there is no depth or stencil to allocate, clear or store, which is the cheapest
/// case a tile-based GPU can get. The passes perform their own layout transitions, so no barrier is
/// recorded around them.
/// </remarks>
internal sealed unsafe class VulkanRenderPasses : IDisposable
{
    private readonly VulkanDevice device;
    private readonly Dictionary<VkFormat, VkRenderPass> swapchainPasses = new Dictionary<VkFormat, VkRenderPass>();

    public VulkanRenderPasses(VulkanDevice device)
    {
        this.device = device;
    }

    /// <summary>
    /// The pass that opens every frame on a swapchain image: clears it, and leaves it ready to present.
    /// </summary>
    public VkRenderPass GetSwapchainPass(VkFormat format)
    {
        if (swapchainPasses.TryGetValue(format, out var pass))
            return pass;

        var attachment = new VkAttachmentDescription
        {
            format = format,
            samples = VkSampleCountFlags.Count1,
            loadOp = VkAttachmentLoadOp.Clear,
            storeOp = VkAttachmentStoreOp.Store,
            stencilLoadOp = VkAttachmentLoadOp.DontCare,
            stencilStoreOp = VkAttachmentStoreOp.DontCare,
            // The previous contents are cleared anyway, so the image's old layout doesn't matter.
            initialLayout = VkImageLayout.Undefined,
            finalLayout = VkImageLayout.PresentSrcKHR,
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

        // The image-available semaphore is waited on at the color-attachment-output stage, so the
        // transition out of UNDEFINED (and the clear) must not happen before that stage either.
        // Without this dependency the implicit one at the start of the pass sits at TOP_OF_PIPE, ahead
        // of the semaphore wait, and the layout transition races the presentation engine still
        // reading the image.
        var dependency = new VkSubpassDependency
        {
            srcSubpass = Vortice.Vulkan.Vulkan.VK_SUBPASS_EXTERNAL,
            dstSubpass = 0,
            srcStageMask = VkPipelineStageFlags.ColorAttachmentOutput,
            dstStageMask = VkPipelineStageFlags.ColorAttachmentOutput,
            srcAccessMask = VkAccessFlags.None,
            dstAccessMask = VkAccessFlags.ColorAttachmentWrite,
        };

        var createInfo = new VkRenderPassCreateInfo
        {
            attachmentCount = 1,
            pAttachments = &attachment,
            subpassCount = 1,
            pSubpasses = &subpass,
            dependencyCount = 1,
            pDependencies = &dependency,
        };

        VulkanDevice.Check(device.Api.vkCreateRenderPass(&createInfo, null, &pass), "vkCreateRenderPass");
        device.SetName(VkObjectType.RenderPass, pass.Handle, $"Swapchain pass ({format})");

        swapchainPasses[format] = pass;
        return pass;
    }

    /// <summary>
    /// Assumes the device is idle.
    /// </summary>
    public void Dispose()
    {
        foreach (var pass in swapchainPasses.Values)
            device.Api.vkDestroyRenderPass(pass, null);

        swapchainPasses.Clear();
    }
}
