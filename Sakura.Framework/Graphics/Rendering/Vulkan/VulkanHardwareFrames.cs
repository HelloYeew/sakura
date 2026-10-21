// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using FFmpeg.AutoGen;
using Vortice.Vulkan;

namespace Sakura.Framework.Graphics.Rendering.Vulkan;

/// <summary>
/// The hardware-decoded frames one frame samples, and their part in its submission.
/// </summary>
/// <remarks>
/// <para>
/// FFmpeg keeps each frame image's state in its <see cref="AVVkFrame"/>: the layout and access the last
/// submission left it in, and a timeline semaphore with the value at which it is ready. Whoever submits
/// work on the image waits on that value and signals the next, so the decoder and the renderer take turns
/// on it without either knowing what the other recorded.
/// </para>
/// <para>
/// A frame that is also a reference picture is decoded from again while it's on screen, which moves its
/// image to another layout between two draws of it. So the state is read under FFmpeg's frame lock
/// for every submission that samples the frame, never remembered from the last one, and the lock is held
/// until that submission has been made.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanHardwareFrames
{
    private const uint queue_family_ignored = Vortice.Vulkan.Vulkan.VK_QUEUE_FAMILY_IGNORED;

    private readonly VulkanDevice device;

    /// <summary>
    /// The <see cref="AVFrame"/>s the frame being recorded samples, each once. Their owners keep them alive
    /// until this frame has finished on the GPU.
    /// </summary>
    private readonly List<nint> frames = new List<nint>();

    private VkSemaphore[] semaphores = new VkSemaphore[4];
    private ulong[] waitValues = new ulong[4];
    private int semaphoreCount;

    public VulkanHardwareFrames(VulkanDevice device)
    {
        this.device = device;
    }

    public bool IsEmpty => frames.Count == 0;

    /// <summary>
    /// The timeline semaphores the submission waits on, at <see cref="WaitValues"/>, and signals one past
    /// them. Filled by <see cref="Begin"/>.
    /// </summary>
    public ReadOnlySpan<VkSemaphore> Semaphores => semaphores.AsSpan(0, semaphoreCount);

    public ReadOnlySpan<ulong> WaitValues => waitValues.AsSpan(0, semaphoreCount);

    /// <summary>
    /// Records that the frame being recorded samples <paramref name="frame"/>. Draw thread only.
    /// </summary>
    public void Use(AVFrame* frame)
    {
        if (!frames.Contains((nint)frame))
            frames.Add((nint)frame);
    }

    /// <summary>
    /// Locks every frame used, and records each image's move into the layout the shaders read it in.
    /// <see cref="End"/> must follow once the submission has been made, or has failed.
    /// </summary>
    /// <param name="commandBuffer">Runs before every draw of the frame (the frame's upload commands).</param>
    public void Begin(VkCommandBuffer commandBuffer)
    {
        semaphoreCount = 0;

        foreach (nint pointer in frames)
        {
            var frame = (AVFrame*)pointer;
            var vkFrame = (AVVkFrame*)frame->data[0];

            lockFrame(frame, vkFrame);

            for (int i = 0; i < 8 && vkFrame->img[i] != 0; i++)
            {
                transition(commandBuffer, new VkImage(vkFrame->img[i]), (VkImageLayout)vkFrame->layout[i]);
                add(new VkSemaphore(vkFrame->sem[i]), vkFrame->sem_value[i]);
            }
        }
    }

    /// <summary>
    /// Stores what a successful submission left each image in (its layout, and the semaphore value that
    /// marks the end of the renderer's use), then unlocks every frame and starts the list over.
    /// </summary>
    public void End(bool submitted)
    {
        foreach (nint pointer in frames)
        {
            var frame = (AVFrame*)pointer;
            var vkFrame = (AVVkFrame*)frame->data[0];

            if (submitted)
            {
                for (int i = 0; i < 8 && vkFrame->img[i] != 0; i++)
                {
                    vkFrame->sem_value[i]++;
                    vkFrame->layout[i] = (int)VkImageLayout.ShaderReadOnlyOptimal;
                    vkFrame->access[i] = (uint)VkAccessFlags.ShaderRead;
                }
            }

            unlockFrame(frame, vkFrame);
        }

        frames.Clear();
        semaphoreCount = 0;
    }

    private void transition(VkCommandBuffer commandBuffer, VkImage image, VkImageLayout layout)
    {
        // The wait on the frame's semaphore, at every stage, is what orders this after the decoder's
        // writes; the barrier only has to follow it.
        var barrier = new VkImageMemoryBarrier
        {
            srcAccessMask = VkAccessFlags.None,
            dstAccessMask = VkAccessFlags.ShaderRead,
            oldLayout = layout,
            newLayout = VkImageLayout.ShaderReadOnlyOptimal,
            // Shared between the decoder's families and the renderer's, or owned by the renderer's alone:
            // either way no ownership changes hands. VulkanRenderer.CanSampleHardwareFrame turns away
            // anything else.
            srcQueueFamilyIndex = queue_family_ignored,
            dstQueueFamilyIndex = queue_family_ignored,
            image = image,
            subresourceRange = new VkImageSubresourceRange
            {
                // The whole image, every plane of it.
                aspectMask = VkImageAspectFlags.Color,
                baseMipLevel = 0,
                levelCount = 1,
                baseArrayLayer = 0,
                layerCount = Vortice.Vulkan.Vulkan.VK_REMAINING_ARRAY_LAYERS,
            },
        };

        device.Api.vkCmdPipelineBarrier(commandBuffer, VkPipelineStageFlags.AllCommands, VkPipelineStageFlags.FragmentShader, VkDependencyFlags.None, 0, null, 0, null, 1, &barrier);
    }

    private void add(VkSemaphore semaphore, ulong value)
    {
        if (semaphoreCount == semaphores.Length)
        {
            Array.Resize(ref semaphores, semaphores.Length * 2);
            Array.Resize(ref waitValues, waitValues.Length * 2);
        }

        semaphores[semaphoreCount] = semaphore;
        waitValues[semaphoreCount] = value;
        semaphoreCount++;
    }

    private static void lockFrame(AVFrame* frame, AVVkFrame* vkFrame)
    {
        var framesContext = (AVHWFramesContext*)frame->hw_frames_ctx->data;
        var vulkanFrames = (AVVulkanFramesContext*)framesContext->hwctx;
        ((delegate* unmanaged[Cdecl]<AVHWFramesContext*, AVVkFrame*, void>)vulkanFrames->lock_frame)(framesContext, vkFrame);
    }

    private static void unlockFrame(AVFrame* frame, AVVkFrame* vkFrame)
    {
        var framesContext = (AVHWFramesContext*)frame->hw_frames_ctx->data;
        var vulkanFrames = (AVVulkanFramesContext*)framesContext->hwctx;
        ((delegate* unmanaged[Cdecl]<AVHWFramesContext*, AVVkFrame*, void>)vulkanFrames->unlock_frame)(framesContext, vkFrame);
    }
}
