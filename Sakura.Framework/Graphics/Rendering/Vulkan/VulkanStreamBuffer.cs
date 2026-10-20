// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Vortice.Vulkan;

namespace Sakura.Framework.Graphics.Rendering.Vulkan;

/// <summary>
/// A persistently mapped buffer that one frame slot carves its per-frame data out of: vertices,
/// indices, uniform blocks, and the bytes of texture uploads.
/// </summary>
/// <remarks>
/// Every write goes to a fresh offset and nothing is ever overwritten within a frame, so a draw never
/// sees data meant for a later one, and no write waits for the GPU. The whole buffer is reused once the
/// slot's fence shows the frame that filled it has finished.
/// </remarks>
internal sealed unsafe class VulkanStreamBuffer : IDisposable
{
    public const VkBufferUsageFlags USAGE = VkBufferUsageFlags.VertexBuffer | VkBufferUsageFlags.IndexBuffer | VkBufferUsageFlags.UniformBuffer | VkBufferUsageFlags.TransferSrc;

    private readonly VulkanDevice device;
    private readonly VulkanMemory memory;
    private readonly VulkanAllocation allocation;

    public VkBuffer Buffer { get; }
    public ulong Size { get; }

    /// <summary>
    /// Bytes handed out since the last <see cref="Reset"/>.
    /// </summary>
    public ulong Used { get; private set; }

    public VulkanStreamBuffer(VulkanDevice device, VulkanMemory memory, ulong size, string name)
    {
        this.device = device;
        this.memory = memory;
        Size = size;

        var createInfo = new VkBufferCreateInfo
        {
            size = size,
            usage = USAGE,
            sharingMode = VkSharingMode.Exclusive,
        };

        VkBuffer buffer;
        VulkanDevice.Check(device.Api.vkCreateBuffer(&createInfo, null, &buffer), "vkCreateBuffer");
        Buffer = buffer;

        allocation = memory.BindHostVisibleBuffer(buffer);
        device.SetName(VkObjectType.Buffer, buffer.Handle, name);
    }

    /// <summary>
    /// Reserves <paramref name="size"/> bytes at an offset aligned to <paramref name="alignment"/>.
    /// </summary>
    /// <returns>False when the rest of the buffer is too small.</returns>
    public bool TryAllocate(ulong size, ulong alignment, out ulong offset, out byte* pointer)
    {
        ulong aligned = alignment <= 1 ? Used : (Used + alignment - 1) / alignment * alignment;

        if (aligned + size > Size)
        {
            offset = 0;
            pointer = null;
            return false;
        }

        Used = aligned + size;
        offset = aligned;
        pointer = allocation.Mapped + aligned;
        return true;
    }

    public void Reset() => Used = 0;

    /// <summary>
    /// Makes this frame's writes visible to the device. Only memory that isn't host-coherent needs it,
    /// and must be called before the submission that reads them.
    /// </summary>
    public void FlushWrites()
    {
        if (allocation.Coherent || Used == 0)
            return;

        var range = new VkMappedMemoryRange
        {
            memory = allocation.Memory,
            offset = 0,
            size = Vortice.Vulkan.Vulkan.VK_WHOLE_SIZE,
        };

        VulkanDevice.Check(device.Api.vkFlushMappedMemoryRanges(1, &range), "vkFlushMappedMemoryRanges");
    }

    public void Dispose()
    {
        device.Api.vkDestroyBuffer(Buffer, null);
        memory.Free(allocation);
    }
}
