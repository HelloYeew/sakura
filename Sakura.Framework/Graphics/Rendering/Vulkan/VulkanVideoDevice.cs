// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using FFmpeg.AutoGen;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;
using Vortice.Vulkan;

namespace Sakura.Framework.Graphics.Rendering.Vulkan;

/// <summary>
/// Hands the renderer's Vulkan device to FFmpeg, so it decodes video into images the renderer can sample
/// where they are.
/// </summary>
/// <remarks>
/// <para>
/// FFmpeg is given the instance, device, enabled features and extensions, and two queue families: the
/// renderer's graphics family, which it uses to prepare frames and copy them, and the decode family, if
/// the device has one. Frame images are then shared between the two families, so sampling one never
/// needs an ownership transfer.
/// </para>
/// <para>
/// FFmpeg submits to the renderer's graphics queue from the decode thread, so it locks queues through
/// <see cref="VulkanDevice.GetQueueLock"/>, the same locks the renderer holds around its own submissions
/// and presents.
/// </para>
/// </remarks>
internal static unsafe class VulkanVideoDevice
{
    /// <summary>
    /// A new FFmpeg device context on <paramref name="device"/>, or null when this FFmpeg has no Vulkan
    /// support, or it refuses the device. The caller owns the reference.
    /// </summary>
    public static AVBufferRef* Create(VulkanDevice device)
    {
        if (!device.SharesVideoFrames)
            return null;

        FFmpegLibrary.EnsureInitialized();

        AVBufferRef* buffer = ffmpeg.av_hwdevice_ctx_alloc(AVHWDeviceType.AV_HWDEVICE_TYPE_VULKAN);

        if (buffer == null)
        {
            Logger.Verbose("Vulkan video: this FFmpeg was built without Vulkan, so video is decoded elsewhere and copied in.");
            return null;
        }

        var context = (AVHWDeviceContext*)buffer->data;
        var vk = (AVVulkanDeviceContext*)context->hwctx;

        // Owned by the context from here, and freed with it.
        var state = new State(device);
        var handle = GCHandle.Alloc(state);
        context->user_opaque = (void*)GCHandle.ToIntPtr(handle);
        context->free = new AVHWDeviceContext_free_func { Pointer = (nint)(delegate* unmanaged[Cdecl]<AVHWDeviceContext*, void>)&free };

        vk->get_proc_addr = (nint)Vortice.Vulkan.Vulkan.vkGetInstanceProcAddr_ptr;
        vk->inst = device.Instance;
        vk->phys_dev = device.PhysicalDevice;
        vk->act_dev = device.Handle;
        vk->device_features = *device.EnabledFeatures;
        vk->enabled_inst_extensions = state.InstanceExtensions.Pointer;
        vk->nb_enabled_inst_extensions = state.InstanceExtensions.Length;
        vk->enabled_dev_extensions = state.DeviceExtensions.Pointer;
        vk->nb_enabled_dev_extensions = state.DeviceExtensions.Length;

        int graphics = (int)device.GraphicsQueueFamily;
        int decode = device.DecodeQueueFamily is uint family ? (int)family : -1;

        // The deprecated per-purpose fields are still checked alongside qf, and must agree with it.
        vk->queue_family_index = graphics;
        vk->nb_graphics_queues = 1;
        vk->queue_family_tx_index = graphics;
        vk->nb_tx_queues = 1;
        vk->queue_family_comp_index = graphics;
        vk->nb_comp_queues = 1;
        vk->queue_family_decode_index = decode;
        vk->nb_decode_queues = decode >= 0 ? 1 : 0;
        vk->queue_family_encode_index = -1;
        vk->nb_encode_queues = 0;

        vk->qf[0] = new AVVulkanDeviceQueueFamily
        {
            idx = graphics,
            num = 1,
            // A graphics family can always transfer, whether or not it reports it.
            flags = device.GraphicsQueueFlags | VkQueueFlags.Transfer,
        };

        vk->nb_qf = 1;

        if (decode >= 0)
        {
            vk->qf[1] = new AVVulkanDeviceQueueFamily
            {
                idx = decode,
                num = 1,
                flags = VkQueueFlags.VideoDecodeKHR,
                video_caps = device.DecodeOperations,
            };

            vk->nb_qf = 2;
        }

        vk->lock_queue = (nint)(delegate* unmanaged[Cdecl]<AVHWDeviceContext*, uint, uint, void>)&lockQueue;
        vk->unlock_queue = (nint)(delegate* unmanaged[Cdecl]<AVHWDeviceContext*, uint, uint, void>)&unlockQueue;

        int result = ffmpeg.av_hwdevice_ctx_init(buffer);

        if (result < 0)
        {
            Logger.Warning($"Vulkan video: FFmpeg couldn't use the renderer's device ({result}), so video is decoded elsewhere and copied in.");
            ffmpeg.av_buffer_unref(&buffer);
            return null;
        }

        return buffer;
    }

    private static State stateOf(AVHWDeviceContext* context) => (State)GCHandle.FromIntPtr((nint)context->user_opaque).Target!;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void lockQueue(AVHWDeviceContext* context, uint family, uint index) => stateOf(context).Device.GetQueueLock(family, index).Enter();

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void unlockQueue(AVHWDeviceContext* context, uint family, uint index) => stateOf(context).Device.GetQueueLock(family, index).Exit();

    /// <summary>
    /// Called by FFmpeg when the last reference to the context goes.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void free(AVHWDeviceContext* context)
    {
        var handle = GCHandle.FromIntPtr((nint)context->user_opaque);
        ((State)handle.Target!).Dispose();
        handle.Free();
        context->user_opaque = null;
    }

    /// <summary>
    /// What the context points at and has to outlive it: the extension name arrays, and the device whose
    /// queue locks the callbacks take.
    /// </summary>
    private sealed class State : IDisposable
    {
        public readonly VulkanDevice Device;
        public readonly NativeStrings InstanceExtensions;
        public readonly NativeStrings DeviceExtensions;

        public State(VulkanDevice device)
        {
            Device = device;
            InstanceExtensions = new NativeStrings(device.EnabledInstanceExtensions);
            DeviceExtensions = new NativeStrings(device.EnabledDeviceExtensions);
        }

        public void Dispose()
        {
            InstanceExtensions.Dispose();
            DeviceExtensions.Dispose();
        }
    }

    /// <summary>
    /// A native array of NUL-terminated UTF-8 strings.
    /// </summary>
    private sealed class NativeStrings : IDisposable
    {
        public readonly byte** Pointer;
        public readonly int Length;

        public NativeStrings(IReadOnlyList<string> strings)
        {
            Length = strings.Count;
            Pointer = (byte**)NativeMemory.AllocZeroed((nuint)(Math.Max(1, Length) * sizeof(byte*)));

            for (int i = 0; i < Length; i++)
            {
                int bytes = Encoding.UTF8.GetByteCount(strings[i]);
                byte* text = (byte*)NativeMemory.Alloc((nuint)bytes + 1);
                Encoding.UTF8.GetBytes(strings[i], new Span<byte>(text, bytes));
                text[bytes] = 0;
                Pointer[i] = text;
            }
        }

        public void Dispose()
        {
            for (int i = 0; i < Length; i++)
                NativeMemory.Free(Pointer[i]);

            NativeMemory.Free(Pointer);
        }
    }
}
