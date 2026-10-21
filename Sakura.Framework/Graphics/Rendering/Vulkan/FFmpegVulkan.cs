// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using Vortice.Vulkan;

namespace Sakura.Framework.Graphics.Rendering.Vulkan;

// FFmpeg's Vulkan hardware context, from libavutil/hwcontext_vulkan.h. FFmpeg.AutoGen doesn't generate
// these (their fields are Vulkan types), so they are declared here against FFmpeg 8.1 (libavutil 60), the
// version the native libraries are built from. libavutil 60 still compiles in the deprecated fixed-queue
// and queue-locking fields, so they are part of the layout. FFmpegVulkanLayoutTest checks every offset
// against the C headers; a libavutil major bump means checking them again.

/// <summary>
/// One queue family FFmpeg may submit to.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AVVulkanDeviceQueueFamily
{
    public int idx;

    /// <summary>
    /// How many queues of the family FFmpeg may use, from index 0.
    /// </summary>
    public int num;

    public VkQueueFlags flags;
    public VkVideoCodecOperationFlagsKHR video_caps;
}

[InlineArray(64)]
internal struct AVVulkanDeviceQueueFamilies
{
    private AVVulkanDeviceQueueFamily element;
}

/// <summary>
/// <c>AVHWDeviceContext.hwctx</c> for <see cref="AVHWDeviceType.AV_HWDEVICE_TYPE_VULKAN"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct AVVulkanDeviceContext
{
    public void* alloc;

    /// <summary>
    /// <c>vkGetInstanceProcAddr</c>. Set, so FFmpeg loads its functions from the renderer's loader rather
    /// than opening one of its own.
    /// </summary>
    public nint get_proc_addr;

    public VkInstance inst;
    public VkPhysicalDevice phys_dev;
    public VkDevice act_dev;

    /// <summary>
    /// The features the device was created with. The <c>pNext</c> chain must stay alive as long as the
    /// context does.
    /// </summary>
    public VkPhysicalDeviceFeatures2 device_features;

    public byte** enabled_inst_extensions;
    public int nb_enabled_inst_extensions;
    public byte** enabled_dev_extensions;
    public int nb_enabled_dev_extensions;

    // Deprecated in favour of qf, but read alongside it until libavutil 61.
    public int queue_family_index;
    public int nb_graphics_queues;
    public int queue_family_tx_index;
    public int nb_tx_queues;
    public int queue_family_comp_index;
    public int nb_comp_queues;
    public int queue_family_encode_index;
    public int nb_encode_queues;
    public int queue_family_decode_index;
    public int nb_decode_queues;

    /// <summary>
    /// <c>void (*)(AVHWDeviceContext *, uint32_t queue_family, uint32_t index)</c>. FFmpeg calls it before
    /// every submission, which is how it shares a queue with the renderer.
    /// </summary>
    public nint lock_queue;

    public nint unlock_queue;

    public AVVulkanDeviceQueueFamilies qf;
    public int nb_qf;
}

/// <summary>
/// <c>AVHWFramesContext.hwctx</c> for Vulkan frames.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct AVVulkanFramesContext
{
    public VkImageTiling tiling;
    public VkImageUsageFlags usage;
    public void* create_pnext;
    public fixed ulong alloc_pnext[8];
    public int flags;
    public VkImageCreateFlags img_flags;
    public fixed int format[8];
    public int nb_layers;

    /// <summary>
    /// <c>void (*)(AVHWFramesContext *, AVVkFrame *)</c>: held from reading a frame's state until the
    /// submission that uses it has been made.
    /// </summary>
    public nint lock_frame;

    public nint unlock_frame;
}

/// <summary>
/// <c>AVFrame.data[0]</c> of an <see cref="AVPixelFormat.AV_PIX_FMT_VULKAN"/> frame: the image (or one
/// image per plane) the frame lives in, and the state anything submitting work on it must honour.
/// </summary>
/// <remarks>
/// Not allocated here (FFmpeg doesn't make its size part of the ABI), only read and updated in place.
/// The protocol, per image: wait on <see cref="sem"/> at <see cref="sem_value"/>, signal it at one more,
/// then store the incremented value and the layout and access the submission left the image in.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct AVVkFrame
{
    public fixed ulong img[8];
    public VkImageTiling tiling;
    public fixed ulong mem[8];
    public fixed ulong size[8];
    public VkMemoryPropertyFlags flags;
    public fixed uint access[8];
    public fixed int layout[8];
    public fixed ulong sem[8];
    public fixed ulong sem_value[8];
    public void* @internal;
    public fixed long offset[8];
    public fixed uint queue_family[8];
}
