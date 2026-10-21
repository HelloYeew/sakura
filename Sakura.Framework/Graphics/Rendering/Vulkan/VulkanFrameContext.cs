// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Vortice.Vulkan;

namespace Sakura.Framework.Graphics.Rendering.Vulkan;

/// <summary>
/// Everything one frame in flight records into and owns until the GPU has finished it: its command
/// buffers, the fence that says when they are done, the semaphore its swapchain image is acquired
/// with, the stream buffer and descriptor sets its draws read, the staging buffers its large uploads
/// read, and the objects destroyed while it was being recorded.
/// </summary>
internal sealed unsafe class VulkanFrameContext : IDisposable
{
    /// <summary>
    /// The stream buffer a slot starts with. It doubles whenever a frame needs more.
    /// </summary>
    private const ulong initial_stream_size = 4 * 1024 * 1024;

    private const uint sets_per_pool = 128;

    private readonly VulkanDevice device;
    private readonly VulkanMemory memory;
    private readonly VulkanStagingPool stagingPool;
    private readonly int index;

    public VkCommandPool CommandPool { get; }

    /// <summary>
    /// The frame's drawing.
    /// </summary>
    public VkCommandBuffer CommandBuffer { get; }

    /// <summary>
    /// The frame's texture uploads and mipmap generation. Submitted ahead of <see cref="CommandBuffer"/>
    /// in the same batch, so whatever is uploaded during a frame is in place before any of the frame's
    /// draws run, wherever in the frame the upload was recorded.
    /// </summary>
    public VkCommandBuffer UploadCommandBuffer { get; }

    /// <summary>
    /// Whether <see cref="UploadCommandBuffer"/> has been begun this frame, and so has to be submitted.
    /// </summary>
    public bool HasUploads { get; private set; }

    /// <summary>
    /// Signalled when this frame's submission completes. Created signalled, so the first wait on a
    /// frame that never ran returns at once.
    /// </summary>
    public VkFence Fence { get; }

    /// <summary>
    /// Signalled by the swapchain when this frame's image is ready to be rendered into.
    /// </summary>
    public VkSemaphore ImageAvailable { get; }

    public VulkanStreamBuffer Stream { get; private set; }

    /// <summary>
    /// Objects released while this frame was recorded. Vulkan doesn't keep anything alive for work in
    /// flight, so they are only destroyed once <see cref="Fence"/> proves the GPU is done with them.
    /// </summary>
    private readonly List<Action> retired = new List<Action>();

    /// <summary>
    /// Staging buffers this frame's uploads read from, handed back to the pool once it has finished.
    /// </summary>
    private readonly List<VulkanStagingBuffer> staging = new List<VulkanStagingBuffer>();

    private readonly List<VkDescriptorPool> descriptorPools = new List<VkDescriptorPool>();
    private int currentPool;

    public int RetiredCount => retired.Count;

    public VulkanFrameContext(VulkanDevice device, VulkanMemory memory, VulkanStagingPool stagingPool, int index)
    {
        this.device = device;
        this.memory = memory;
        this.stagingPool = stagingPool;
        this.index = index;

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
            commandBufferCount = 2,
        };

        var commandBuffers = stackalloc VkCommandBuffer[2];
        VulkanDevice.Check(api.vkAllocateCommandBuffers(&allocateInfo, commandBuffers), "vkAllocateCommandBuffers");
        CommandBuffer = commandBuffers[0];
        UploadCommandBuffer = commandBuffers[1];

        var fenceInfo = new VkFenceCreateInfo { flags = VkFenceCreateFlags.Signaled };
        VkFence fence;
        VulkanDevice.Check(api.vkCreateFence(&fenceInfo, null, &fence), "vkCreateFence");
        Fence = fence;

        var semaphoreInfo = new VkSemaphoreCreateInfo();
        VkSemaphore semaphore;
        VulkanDevice.Check(api.vkCreateSemaphore(&semaphoreInfo, null, &semaphore), "vkCreateSemaphore");
        ImageAvailable = semaphore;

        Stream = new VulkanStreamBuffer(device, memory, initial_stream_size, $"Frame {index} stream");
        descriptorPools.Add(createDescriptorPool());

        device.SetName(VkObjectType.CommandBuffer, (ulong)CommandBuffer.Handle, $"Frame {index} commands");
        device.SetName(VkObjectType.CommandBuffer, (ulong)UploadCommandBuffer.Handle, $"Frame {index} uploads");
        device.SetName(VkObjectType.Fence, fence.Handle, $"Frame {index} fence");
        device.SetName(VkObjectType.Semaphore, semaphore.Handle, $"Frame {index} image available");
    }

    /// <summary>
    /// Prepares the slot for a new frame. Only valid once <see cref="Fence"/> has signalled.
    /// </summary>
    public void Reset()
    {
        ReleaseRetired();

        var api = device.Api;
        VulkanDevice.Check(api.vkResetCommandPool(CommandPool, VkCommandPoolResetFlags.None), "vkResetCommandPool");

        foreach (var pool in descriptorPools)
            VulkanDevice.Check(api.vkResetDescriptorPool(pool, VkDescriptorPoolResetFlags.None), "vkResetDescriptorPool");

        currentPool = 0;
        Stream.Reset();
        HasUploads = false;
    }

    /// <summary>
    /// Begins <see cref="UploadCommandBuffer"/> on the frame's first upload.
    /// </summary>
    public VkCommandBuffer BeginUploads()
    {
        if (!HasUploads)
        {
            var beginInfo = new VkCommandBufferBeginInfo { flags = VkCommandBufferUsageFlags.OneTimeSubmit };
            VulkanDevice.Check(device.Api.vkBeginCommandBuffer(UploadCommandBuffer, &beginInfo), "vkBeginCommandBuffer");
            HasUploads = true;
        }

        return UploadCommandBuffer;
    }

    /// <summary>
    /// Reserves <paramref name="size"/> bytes of this frame's stream buffer. When the buffer is full, a
    /// buffer twice as large replaces it, and the full one is destroyed once this frame has finished on
    /// the GPU, since commands already recorded still read from it.
    /// </summary>
    /// <returns>Whether the stream buffer had to be replaced, which invalidates anything bound from the
    /// old one.</returns>
    public bool AllocateStream(ulong size, ulong alignment, out ulong offset, out byte* pointer)
    {
        if (Stream.TryAllocate(size, alignment, out offset, out pointer))
            return false;

        var full = Stream;
        full.FlushWrites();
        Retire(full.Dispose);

        ulong newSize = full.Size * 2;

        while (newSize < size + alignment)
            newSize *= 2;

        Stream = new VulkanStreamBuffer(device, memory, newSize, $"Frame {index} stream ({newSize / (1024 * 1024)} MB)");

        if (!Stream.TryAllocate(size, alignment, out offset, out pointer))
            throw new InvalidOperationException($"A {size}-byte stream allocation does not fit in a new {newSize}-byte stream buffer.");

        return true;
    }

    /// <summary>
    /// Allocates a descriptor set that lives until this slot is next reset.
    /// </summary>
    public VkDescriptorSet AllocateDescriptorSet(VkDescriptorSetLayout layout)
    {
        while (true)
        {
            var allocateInfo = new VkDescriptorSetAllocateInfo
            {
                descriptorPool = descriptorPools[currentPool],
                descriptorSetCount = 1,
                pSetLayouts = &layout,
            };

            VkDescriptorSet set;
            var result = device.Api.vkAllocateDescriptorSets(&allocateInfo, &set);

            if (result == VkResult.Success)
                return set;

            if (result != VkResult.ErrorOutOfPoolMemory && result != VkResult.ErrorFragmentedPool)
                VulkanDevice.Check(result, "vkAllocateDescriptorSets");

            // This pool is full for the frame; move on to the next, creating it if none is left.
            currentPool++;

            if (currentPool == descriptorPools.Count)
                descriptorPools.Add(createDescriptorPool());
        }
    }

    /// <summary>
    /// Destroys <paramref name="destroy"/>'s object once this frame has finished on the GPU.
    /// </summary>
    public void Retire(Action destroy) => retired.Add(destroy);

    /// <summary>
    /// Takes a staging buffer for one of this frame's uploads, held until the frame has finished on the
    /// GPU.
    /// </summary>
    public VulkanStagingBuffer RentStaging(ulong size)
    {
        var buffer = stagingPool.Rent(size);
        staging.Add(buffer);
        return buffer;
    }

    /// <summary>
    /// Destroys everything retired into this frame. Only valid once <see cref="Fence"/> has signalled.
    /// </summary>
    public void ReleaseRetired()
    {
        foreach (var destroy in retired)
            destroy();

        retired.Clear();

        foreach (var buffer in staging)
            stagingPool.Return(buffer);

        staging.Clear();
    }

    private VkDescriptorPool createDescriptorPool()
    {
        // Sized so either kind of set can fill the pool on its own: a frame alternating textures uses
        // many texture-array sets, one rebinding uniforms on a grown stream buffer only uniform sets.
        var sizes = stackalloc VkDescriptorPoolSize[2];
        sizes[0] = new VkDescriptorPoolSize(VkDescriptorType.CombinedImageSampler, sets_per_pool * VulkanShaderLayout.TEXTURE_ARRAY_LENGTH);
        sizes[1] = new VkDescriptorPoolSize(VkDescriptorType.UniformBufferDynamic, sets_per_pool * VulkanShaderLayout.UNIFORM_BINDING_COUNT);

        var createInfo = new VkDescriptorPoolCreateInfo
        {
            maxSets = sets_per_pool,
            poolSizeCount = 2,
            pPoolSizes = sizes,
        };

        VkDescriptorPool pool;
        VulkanDevice.Check(device.Api.vkCreateDescriptorPool(&createInfo, null, &pool), "vkCreateDescriptorPool");
        return pool;
    }

    /// <summary>
    /// Assumes the device is idle.
    /// </summary>
    public void Dispose()
    {
        ReleaseRetired();

        var api = device.Api;

        foreach (var pool in descriptorPools)
            api.vkDestroyDescriptorPool(pool, null);

        Stream.Dispose();
        api.vkDestroySemaphore(ImageAvailable, null);
        api.vkDestroyFence(Fence, null);
        api.vkDestroyCommandPool(CommandPool, null);
    }
}
