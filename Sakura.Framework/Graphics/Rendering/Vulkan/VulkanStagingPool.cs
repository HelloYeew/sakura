// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Statistic;
using Vortice.Vulkan;

namespace Sakura.Framework.Graphics.Rendering.Vulkan;

/// <summary>
/// A persistently mapped buffer one large upload is staged in.
/// </summary>
internal sealed unsafe class VulkanStagingBuffer
{
    public readonly VkBuffer Buffer;
    public readonly VulkanAllocation Allocation;
    public readonly ulong Size;

    /// <summary>
    /// When the buffer was last handed back, in <see cref="Environment.TickCount64"/> milliseconds.
    /// </summary>
    public long ReturnedAt;

    public VulkanStagingBuffer(VkBuffer buffer, VulkanAllocation allocation, ulong size)
    {
        Buffer = buffer;
        Allocation = allocation;
        Size = size;
    }

    public byte* Mapped => Allocation.Mapped;
}

/// <summary>
/// Staging buffers for uploads too large for a frame's stream buffer, kept for the next upload once the
/// frame that used one has finished, rather than destroyed.
/// </summary>
/// <remarks>
/// A video stages a whole decoded frame (12 MB at 4K) every time it advances. Allocating memory that
/// large is slow on every driver, and the first write into it also pays to fault its pages in, so the
/// same few buffers go round instead. A buffer nothing has asked for in a few seconds (the video
/// stopped, or it held a one-off large image) is destroyed. Draw thread only.
/// </remarks>
internal sealed unsafe class VulkanStagingPool : IDisposable
{
    private static readonly GlobalStatistic<int> stat_buffers_created = GlobalStatistics.Get<int>("Vulkan", "Staging Buffers Created", StatisticKind.Cumulative);

    private const long idle_milliseconds = 3000;

    /// <summary>
    /// Sizes are rounded up to this, so frames whose size differs by a few rows share buffers.
    /// </summary>
    private const ulong granularity = 1024 * 1024;

    private readonly VulkanDevice device;
    private readonly VulkanMemory memory;
    private readonly List<VulkanStagingBuffer> free = new List<VulkanStagingBuffer>();

    public VulkanStagingPool(VulkanDevice device, VulkanMemory memory)
    {
        this.device = device;
        this.memory = memory;
    }

    /// <summary>
    /// A buffer of at least <paramref name="size"/> bytes: the smallest free one that is no more than
    /// twice that, or a new one.
    /// </summary>
    public VulkanStagingBuffer Rent(ulong size)
    {
        int best = -1;

        for (int i = 0; i < free.Count; i++)
        {
            ulong candidate = free[i].Size;

            if (candidate < size || candidate / 2 > size)
                continue;

            if (best < 0 || candidate < free[best].Size)
                best = i;
        }

        if (best >= 0)
        {
            var buffer = free[best];
            free.RemoveAt(best);
            return buffer;
        }

        return create((size + granularity - 1) / granularity * granularity);
    }

    /// <summary>
    /// Hands back a buffer no command still reads from.
    /// </summary>
    public void Return(VulkanStagingBuffer buffer)
    {
        buffer.ReturnedAt = Environment.TickCount64;
        free.Add(buffer);
    }

    /// <summary>
    /// Destroys the buffers nothing has asked for in a while.
    /// </summary>
    public void Trim()
    {
        long now = Environment.TickCount64;

        for (int i = free.Count - 1; i >= 0; i--)
        {
            if (now - free[i].ReturnedAt < idle_milliseconds)
                continue;

            destroy(free[i]);
            free.RemoveAt(i);
        }
    }

    /// <summary>
    /// Destroys every free buffer. Rented ones must have been returned first.
    /// </summary>
    public void Dispose()
    {
        foreach (var buffer in free)
            destroy(buffer);

        free.Clear();
    }

    private VulkanStagingBuffer create(ulong size)
    {
        var createInfo = new VkBufferCreateInfo
        {
            size = size,
            usage = VkBufferUsageFlags.TransferSrc,
            sharingMode = VkSharingMode.Exclusive,
        };

        VkBuffer buffer;
        VulkanDevice.Check(device.Api.vkCreateBuffer(&createInfo, null, &buffer), "vkCreateBuffer");

        var allocation = memory.BindHostVisibleBuffer(buffer);
        device.SetName(VkObjectType.Buffer, buffer.Handle, $"Staging ({size / granularity} MB)");
        stat_buffers_created.Value++;

        return new VulkanStagingBuffer(buffer, allocation, size);
    }

    private void destroy(VulkanStagingBuffer buffer)
    {
        device.Api.vkDestroyBuffer(buffer.Buffer, null);
        memory.Free(buffer.Allocation);
    }
}
