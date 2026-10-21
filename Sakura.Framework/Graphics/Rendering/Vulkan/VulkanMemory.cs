// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Threading;
using Sakura.Framework.Statistic;
using Vortice.Vulkan;

namespace Sakura.Framework.Graphics.Rendering.Vulkan;

/// <summary>
/// A range of device memory owned by one image or buffer.
/// </summary>
internal readonly unsafe struct VulkanAllocation
{
    public readonly VkDeviceMemory Memory;
    public readonly ulong Offset;
    public readonly ulong Size;

    /// <summary>
    /// The host address of the range, for memory mapped at allocation; null otherwise.
    /// </summary>
    public readonly byte* Mapped;

    /// <summary>
    /// Whether host writes are visible to the device without a flush.
    /// </summary>
    public readonly bool Coherent;

    /// <summary>
    /// The block the range was carved from, or null for an allocation of its own.
    /// </summary>
    internal readonly VulkanMemory.Block? Block;

    public VulkanAllocation(VkDeviceMemory memory, ulong offset, ulong size, byte* mapped, bool coherent, VulkanMemory.Block? block)
    {
        Memory = memory;
        Offset = offset;
        Size = size;
        Mapped = mapped;
        Coherent = coherent;
        Block = block;
    }

    public bool IsNull => Memory.IsNull;
}

/// <summary>
/// Device memory for images and buffers.
/// </summary>
/// <remarks>
/// <para>
/// Drivers cap how many allocations may exist at once (the spec only promises 4096), and each one is
/// slow to make, so image memory is carved out of large blocks instead. Only optimal-tiling images
/// share blocks, which keeps linear and optimal resources apart and makes
/// <c>bufferImageGranularity</c> irrelevant. Buffers are few and long-lived (the per-frame stream
/// buffers, the quad index buffer, upload staging for very large images) and get allocations of their
/// own, as do images too big to share a block sensibly.
/// </para>
/// <para>
/// Thread-safe: textures are created off the draw thread.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanMemory : IDisposable
{
    private const ulong block_size = 64 * 1024 * 1024;

    /// <summary>
    /// Images at least this large get an allocation of their own rather than a quarter or more of a
    /// block, where they would fragment it.
    /// </summary>
    private const ulong dedicated_threshold = block_size / 4;

    private static readonly GlobalStatistic<long> stat_device_memory = GlobalStatistics.Get<long>("Vulkan", "Device Memory", StatisticKind.Gauge, StatisticUnit.Bytes);
    private static readonly GlobalStatistic<int> stat_allocations = GlobalStatistics.Get<int>("Vulkan", "Device Allocations");

    private readonly VulkanDevice device;
    private readonly VkPhysicalDeviceMemoryProperties properties;
    private readonly Lock gate = new Lock();
    private readonly List<Block>[] blocks = new List<Block>[32];

    private long allocatedBytes;
    private int allocationCount;

    public VulkanMemory(VulkanDevice device)
    {
        this.device = device;

        VkPhysicalDeviceMemoryProperties memoryProperties;
        device.InstanceApi.vkGetPhysicalDeviceMemoryProperties(device.PhysicalDevice, &memoryProperties);
        properties = memoryProperties;

        for (int i = 0; i < blocks.Length; i++)
            blocks[i] = new List<Block>();
    }

    /// <summary>
    /// Allocates device-local memory for <paramref name="image"/> and binds it.
    /// </summary>
    public VulkanAllocation BindImage(VkImage image)
    {
        VkMemoryRequirements requirements;
        device.Api.vkGetImageMemoryRequirements(image, &requirements);

        uint type = findType(requirements.memoryTypeBits, VkMemoryPropertyFlags.DeviceLocal, VkMemoryPropertyFlags.None)
                    ?? findType(requirements.memoryTypeBits, VkMemoryPropertyFlags.None, VkMemoryPropertyFlags.None)
                    ?? throw new InvalidOperationException("No memory type can hold this image.");

        var allocation = requirements.size >= dedicated_threshold
            ? allocateDedicated(type, requirements.size, false)
            : suballocate(type, requirements.size, requirements.alignment);

        VulkanDevice.Check(device.Api.vkBindImageMemory(image, allocation.Memory, allocation.Offset), "vkBindImageMemory");
        return allocation;
    }

    /// <summary>
    /// Allocates host-visible memory for <paramref name="buffer"/>, binds it, and maps it for the
    /// allocation's lifetime. Device-local host-visible memory is preferred where it exists (unified
    /// memory on phones and Apple GPUs, resizable BAR on desktops), since the GPU then reads it at full
    /// speed.
    /// </summary>
    public VulkanAllocation BindHostVisibleBuffer(VkBuffer buffer)
    {
        VkMemoryRequirements requirements;
        device.Api.vkGetBufferMemoryRequirements(buffer, &requirements);

        uint type = findType(requirements.memoryTypeBits, VkMemoryPropertyFlags.HostVisible, VkMemoryPropertyFlags.DeviceLocal | VkMemoryPropertyFlags.HostCoherent)
                    ?? throw new InvalidOperationException("No host-visible memory type can hold this buffer.");

        var allocation = allocateDedicated(type, requirements.size, true);
        VulkanDevice.Check(device.Api.vkBindBufferMemory(buffer, allocation.Memory, 0), "vkBindBufferMemory");
        return allocation;
    }

    /// <summary>
    /// Allocates device-local memory for <paramref name="buffer"/> and binds it. Its contents can only be
    /// written by transfers.
    /// </summary>
    public VulkanAllocation BindDeviceLocalBuffer(VkBuffer buffer)
    {
        VkMemoryRequirements requirements;
        device.Api.vkGetBufferMemoryRequirements(buffer, &requirements);

        uint type = findType(requirements.memoryTypeBits, VkMemoryPropertyFlags.DeviceLocal, VkMemoryPropertyFlags.None)
                    ?? findType(requirements.memoryTypeBits, VkMemoryPropertyFlags.None, VkMemoryPropertyFlags.None)
                    ?? throw new InvalidOperationException("No memory type can hold this buffer.");

        var allocation = allocateDedicated(type, requirements.size, false);
        VulkanDevice.Check(device.Api.vkBindBufferMemory(buffer, allocation.Memory, 0), "vkBindBufferMemory");
        return allocation;
    }

    /// <summary>
    /// Returns <paramref name="allocation"/>'s memory. Whatever was bound to it must already be
    /// destroyed.
    /// </summary>
    public void Free(in VulkanAllocation allocation)
    {
        if (allocation.IsNull)
            return;

        lock (gate)
        {
            if (allocation.Block is not Block block)
            {
                freeMemory(allocation.Memory, allocation.Size);
                return;
            }

            block.Release(allocation.Offset, allocation.Size);

            // An empty block goes back to the driver unless it is the last one of its type, which is
            // kept so a texture freed and reallocated each frame doesn't allocate a block each time.
            var typeBlocks = blocks[block.MemoryType];

            if (block.IsEmpty && typeBlocks.Count > 1)
            {
                typeBlocks.Remove(block);
                freeMemory(block.Memory, block.Size);
            }
        }
    }

    /// <summary>
    /// Releases every block. Everything allocated from them must already be destroyed.
    /// </summary>
    public void Dispose()
    {
        lock (gate)
        {
            foreach (var typeBlocks in blocks)
            {
                foreach (var block in typeBlocks)
                    freeMemory(block.Memory, block.Size);

                typeBlocks.Clear();
            }
        }
    }

    private VulkanAllocation suballocate(uint type, ulong size, ulong alignment)
    {
        lock (gate)
        {
            var typeBlocks = blocks[type];

            foreach (var block in typeBlocks)
            {
                if (block.TryAllocate(size, alignment, out ulong offset))
                    return new VulkanAllocation(block.Memory, offset, size, null, false, block);
            }

            var memory = allocateMemory(type, block_size);
            var newBlock = new Block(memory, block_size, type);
            typeBlocks.Add(newBlock);

            if (!newBlock.TryAllocate(size, alignment, out ulong newOffset))
                throw new InvalidOperationException($"A {size}-byte allocation does not fit in a new {block_size}-byte block.");

            return new VulkanAllocation(memory, newOffset, size, null, false, newBlock);
        }
    }

    private VulkanAllocation allocateDedicated(uint type, ulong size, bool map)
    {
        lock (gate)
        {
            var memory = allocateMemory(type, size);
            byte* mapped = null;

            if (map)
            {
                void* pointer;
                VulkanDevice.Check(device.Api.vkMapMemory(memory, 0, Vortice.Vulkan.Vulkan.VK_WHOLE_SIZE, 0, &pointer), "vkMapMemory");
                mapped = (byte*)pointer;
            }

            bool coherent = (memoryTypeFlags(type) & VkMemoryPropertyFlags.HostCoherent) != 0;
            return new VulkanAllocation(memory, 0, size, mapped, coherent, null);
        }
    }

    private VkDeviceMemory allocateMemory(uint type, ulong size)
    {
        var allocateInfo = new VkMemoryAllocateInfo
        {
            allocationSize = size,
            memoryTypeIndex = type,
        };

        VkDeviceMemory memory;
        var result = device.Api.vkAllocateMemory(&allocateInfo, null, &memory);

        if (result != VkResult.Success)
            throw new OutOfMemoryException($"vkAllocateMemory of {size} bytes (memory type {type}) failed: {result}.");

        allocatedBytes += (long)size;
        allocationCount++;
        stat_device_memory.Value = allocatedBytes;
        stat_allocations.Value = allocationCount;

        return memory;
    }

    private void freeMemory(VkDeviceMemory memory, ulong size)
    {
        // Mapped memory is unmapped implicitly when freed.
        device.Api.vkFreeMemory(memory, null);

        allocatedBytes -= (long)size;
        allocationCount--;
        stat_device_memory.Value = allocatedBytes;
        stat_allocations.Value = allocationCount;
    }

    private VkMemoryPropertyFlags memoryTypeFlags(uint type) => properties.memoryTypes[(int)type].propertyFlags;

    /// <summary>
    /// The memory type allowed by <paramref name="typeBits"/> that has every <paramref name="required"/>
    /// flag and the most <paramref name="preferred"/> ones, lowest index first on a tie.
    /// </summary>
    private uint? findType(uint typeBits, VkMemoryPropertyFlags required, VkMemoryPropertyFlags preferred)
    {
        uint? best = null;
        int bestScore = -1;

        for (uint i = 0; i < properties.memoryTypeCount; i++)
        {
            if ((typeBits & (1u << (int)i)) == 0)
                continue;

            var flags = memoryTypeFlags(i);

            if ((flags & required) != required)
                continue;

            int score = System.Numerics.BitOperations.PopCount((uint)(flags & preferred));

            if (score > bestScore)
            {
                best = i;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>
    /// One <c>VkDeviceMemory</c> handed out in ranges, first fit, with neighbouring free ranges merged
    /// back together.
    /// </summary>
    internal sealed class Block
    {
        public readonly VkDeviceMemory Memory;
        public readonly ulong Size;
        public readonly uint MemoryType;

        /// <summary>
        /// Free ranges, sorted by offset and never adjacent (adjacent ones are merged).
        /// </summary>
        private readonly List<(ulong Offset, ulong Size)> free = new List<(ulong, ulong)>();

        public Block(VkDeviceMemory memory, ulong size, uint memoryType)
        {
            Memory = memory;
            Size = size;
            MemoryType = memoryType;
            free.Add((0, size));
        }

        public bool IsEmpty => free.Count == 1 && free[0].Size == Size;

        public bool TryAllocate(ulong size, ulong alignment, out ulong offset)
        {
            for (int i = 0; i < free.Count; i++)
            {
                var (start, length) = free[i];
                ulong aligned = alignUp(start, alignment);
                ulong padding = aligned - start;

                if (padding + size > length)
                    continue;

                // The alignment padding in front stays free, as does whatever is left behind.
                free.RemoveAt(i);

                if (padding > 0)
                    free.Insert(i++, (start, padding));

                ulong remaining = length - padding - size;

                if (remaining > 0)
                    free.Insert(i, (aligned + size, remaining));

                offset = aligned;
                return true;
            }

            offset = 0;
            return false;
        }

        public void Release(ulong offset, ulong size)
        {
            int index = 0;

            while (index < free.Count && free[index].Offset < offset)
                index++;

            free.Insert(index, (offset, size));

            // Merge with the following range, then the preceding one.
            if (index + 1 < free.Count && free[index].Offset + free[index].Size == free[index + 1].Offset)
            {
                free[index] = (free[index].Offset, free[index].Size + free[index + 1].Size);
                free.RemoveAt(index + 1);
            }

            if (index > 0 && free[index - 1].Offset + free[index - 1].Size == free[index].Offset)
            {
                free[index - 1] = (free[index - 1].Offset, free[index - 1].Size + free[index].Size);
                free.RemoveAt(index);
            }
        }

        private static ulong alignUp(ulong value, ulong alignment) => alignment <= 1 ? value : (value + alignment - 1) / alignment * alignment;
    }
}
