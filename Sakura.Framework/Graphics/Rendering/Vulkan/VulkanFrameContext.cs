// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Vortice.Vulkan;

namespace Sakura.Framework.Graphics.Rendering.Vulkan;

/// <summary>
/// Everything one frame in flight records into and owns until the GPU has finished it: a command
/// buffer, the fence that says when it is done, the semaphore its swapchain image is acquired with,
/// and the objects destroyed while it was being recorded.
/// </summary>
internal sealed unsafe class VulkanFrameContext : IDisposable
{
    private readonly VulkanDevice device;

    public VkCommandPool CommandPool { get; }
    public VkCommandBuffer CommandBuffer { get; }

    /// <summary>
    /// Signalled when this frame's submission completes. Created signalled, so the first wait on a
    /// frame that never ran returns at once.
    /// </summary>
    public VkFence Fence { get; }

    /// <summary>
    /// Signalled by the swapchain when this frame's image is ready to be rendered into.
    /// </summary>
    public VkSemaphore ImageAvailable { get; }

    /// <summary>
    /// Objects released while this frame was recorded. Vulkan doesn't keep anything alive for work in
    /// flight, so they are only destroyed once <see cref="Fence"/> proves the GPU is done with them.
    /// </summary>
    private readonly List<Action> retired = new List<Action>();

    public VulkanFrameContext(VulkanDevice device, int index)
    {
        this.device = device;
        var api = device.Api;

        var poolInfo = new VkCommandPoolCreateInfo
        {
            // Reset as a whole every frame, never per buffer.
            flags = VkCommandPoolCreateFlags.Transient,
            queueFamilyIndex = device.GraphicsQueueFamily,
        };

        VkCommandPool pool;
        VulkanDevice.Check(api.vkCreateCommandPool(&poolInfo, null, &pool), "vkCreateCommandPool");
        CommandPool = pool;

        var allocateInfo = new VkCommandBufferAllocateInfo
        {
            commandPool = pool,
            level = VkCommandBufferLevel.Primary,
            commandBufferCount = 1,
        };

        VkCommandBuffer commandBuffer;
        VulkanDevice.Check(api.vkAllocateCommandBuffers(&allocateInfo, &commandBuffer), "vkAllocateCommandBuffers");
        CommandBuffer = commandBuffer;

        var fenceInfo = new VkFenceCreateInfo { flags = VkFenceCreateFlags.Signaled };
        VkFence fence;
        VulkanDevice.Check(api.vkCreateFence(&fenceInfo, null, &fence), "vkCreateFence");
        Fence = fence;

        var semaphoreInfo = new VkSemaphoreCreateInfo();
        VkSemaphore semaphore;
        VulkanDevice.Check(api.vkCreateSemaphore(&semaphoreInfo, null, &semaphore), "vkCreateSemaphore");
        ImageAvailable = semaphore;

        device.SetName(VkObjectType.CommandBuffer, (ulong)commandBuffer.Handle, $"Frame {index} commands");
        device.SetName(VkObjectType.Fence, fence.Handle, $"Frame {index} fence");
        device.SetName(VkObjectType.Semaphore, semaphore.Handle, $"Frame {index} image available");
    }

    /// <summary>
    /// Destroys <paramref name="destroy"/>'s object once this frame has finished on the GPU.
    /// </summary>
    public void Retire(Action destroy) => retired.Add(destroy);

    /// <summary>
    /// Destroys everything retired into this frame. Only valid once <see cref="Fence"/> has signalled.
    /// </summary>
    public void ReleaseRetired()
    {
        foreach (var destroy in retired)
            destroy();

        retired.Clear();
    }

    /// <summary>
    /// Assumes the device is idle.
    /// </summary>
    public void Dispose()
    {
        ReleaseRetired();

        var api = device.Api;
        api.vkDestroySemaphore(ImageAvailable, null);
        api.vkDestroyFence(Fence, null);
        api.vkDestroyCommandPool(CommandPool, null);
    }
}
