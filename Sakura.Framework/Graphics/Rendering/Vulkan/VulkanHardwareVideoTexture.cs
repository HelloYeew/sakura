// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Threading;
using FFmpeg.AutoGen;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Logging;
using Sakura.Framework.Statistic;
using Vortice.Vulkan;

namespace Sakura.Framework.Graphics.Rendering.Vulkan;

/// <summary>
/// The zero-copy counterpart of <see cref="VulkanVideoTexture"/>: samples the image FFmpeg's Vulkan
/// decoder produced on the renderer's own device, through a view of each of its two
/// <see cref="VideoPlaneLayout.Nv12"/> planes, instead of copying the frame anywhere.
/// </summary>
/// <remarks>
/// It holds a reference to the frame it shows, which keeps the decoder from reusing the frame's image
/// until it is replaced and the GPU has finished with it. Every frame that draws it acquires the image
/// for that submission (<see cref="VulkanHardwareFrames"/>), since the decoder may have used it in
/// between.
/// </remarks>
public sealed unsafe class VulkanHardwareVideoTexture : INativeVideoTexture
{
    /// <summary>
    /// A frame whose planes could not be viewed, so nothing was bound. Non-zero here means video is
    /// frozen on its previous frame.
    /// </summary>
    private static readonly GlobalStatistic<long> stat_map_failures = GlobalStatistics.Get<long>("Video", "Zero-Copy Map Failures", StatisticKind.Cumulative);

    private readonly VulkanRenderer renderer;
    private readonly VulkanDevice device;

    /// <summary>
    /// This texture's own reference to the frame it shows; null until the first frame lands.
    /// </summary>
    private AVFrame* frame;

    private VkImageView lumaView;
    private VkImageView chromaView;

    public int Width { get; }
    public int Height { get; }

    public VideoPlaneLayout Layout => VideoPlaneLayout.Nv12;

    /// <summary>
    /// Whether a frame has been mapped and is ready to draw. Written on the draw thread, read on the
    /// update thread.
    /// </summary>
    public bool Available => Volatile.Read(ref available);

    private bool available;

    /// <summary>
    /// Zero: the planes belong to the decoder's frame pool. Charging for them would count the decoder's
    /// memory twice.
    /// </summary>
    public long PlaneBytes => 0;

    public TextureBindCounter Binds { get; } = new TextureBindCounter();

    private bool disposed;

    internal VulkanHardwareVideoTexture(VulkanRenderer renderer, VulkanDevice device, int width, int height)
    {
        this.renderer = renderer;
        this.device = device;
        Width = width;
        Height = height;

        renderer.Track(this);
    }

    /// <summary>
    /// Puts the planes at texture slots 0 and 1, <c>video_nv12.frag</c>'s bindings in set 1, and has the
    /// frame acquired for this frame's submission. <paramref name="tiling"/> samples them repeating rather
    /// than clamped. Draw thread only.
    /// </summary>
    public void BindPlanes(bool tiling)
    {
        Binds.Record();

        if (frame == null)
            return;

        renderer.BindHardwarePlanes(frame, lumaView, chromaView, tiling);
    }

    /// <summary>
    /// Takes over <paramref name="source"/>, an <c>AV_PIX_FMT_VULKAN</c> frame decoded on the renderer's
    /// device: views its planes and keeps a reference to it. Nothing is copied. Draw thread only.
    /// </summary>
    public void Upload(AVFrame* source)
    {
        var vkFrame = (AVVkFrame*)source->data[0];

        if (vkFrame == null || vkFrame->img[0] == 0)
        {
            mapFailed("the frame carried no image");
            return;
        }

        // One multi-planar image, viewed a plane at a time, or one image per plane.
        bool imagePerPlane = vkFrame->img[1] != 0;
        var lumaImage = new VkImage(vkFrame->img[0]);
        var chromaImage = imagePerPlane ? new VkImage(vkFrame->img[1]) : lumaImage;

        VkImageView newLuma = VkImageView.Null;
        VkImageView newChroma = VkImageView.Null;
        AVFrame* reference = null;

        try
        {
            newLuma = createView(lumaImage, VkFormat.R8Unorm, imagePerPlane ? VkImageAspectFlags.Color : VkImageAspectFlags.Plane0);
            newChroma = createView(chromaImage, VkFormat.R8G8Unorm, imagePerPlane ? VkImageAspectFlags.Color : VkImageAspectFlags.Plane1);

            reference = ffmpeg.av_frame_alloc();

            if (reference == null || ffmpeg.av_frame_ref(reference, source) < 0)
                throw new InvalidOperationException("av_frame_ref failed");
        }
        catch (Exception e)
        {
            if (newLuma.IsNotNull) device.Api.vkDestroyImageView(newLuma, null);
            if (newChroma.IsNotNull) device.Api.vkDestroyImageView(newChroma, null);
            if (reference != null) ffmpeg.av_frame_free(&reference);

            mapFailed($"{e.Message} for {source->width}x{source->height}");
            return;
        }

        // Only now is the previous frame let go, and only once the GPU is done with it: a failure above
        // leaves the previous frame on screen rather than nothing.
        release();

        frame = reference;
        lumaView = newLuma;
        chromaView = newChroma;

        MarkAvailable();
    }

    public void MarkAvailable() => Volatile.Write(ref available, true);

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        Volatile.Write(ref available, false);

        release();
        renderer.Untrack(this);
    }

    private VkImageView createView(VkImage image, VkFormat format, VkImageAspectFlags aspect)
    {
        // The image carries decode and storage usages the plane format can't support; the view is only
        // ever sampled.
        var usage = new VkImageViewUsageCreateInfo { usage = VkImageUsageFlags.Sampled };

        var createInfo = new VkImageViewCreateInfo
        {
            pNext = &usage,
            image = image,
            viewType = VkImageViewType.Image2D,
            format = format,
            components = new VkComponentMapping
            {
                r = VkComponentSwizzle.Identity,
                g = VkComponentSwizzle.Identity,
                b = VkComponentSwizzle.Identity,
                a = VkComponentSwizzle.Identity,
            },
            subresourceRange = new VkImageSubresourceRange
            {
                aspectMask = aspect,
                levelCount = 1,
                layerCount = 1,
            },
        };

        VkImageView view;
        VulkanDevice.Check(device.Api.vkCreateImageView(&createInfo, null, &view), "vkCreateImageView");
        return view;
    }

    /// <summary>
    /// Lets go of the current frame and its views once no frame that may sample them is still running.
    /// </summary>
    private void release()
    {
        if (frame == null)
            return;

        var releasedFrame = frame;
        var releasedLuma = lumaView;
        var releasedChroma = chromaView;

        frame = null;
        lumaView = VkImageView.Null;
        chromaView = VkImageView.Null;

        renderer.Retire(() =>
        {
            device.Api.vkDestroyImageView(releasedLuma, null);
            device.Api.vkDestroyImageView(releasedChroma, null);

            var freed = releasedFrame;
            ffmpeg.av_frame_free(&freed);
        });
    }

    private static void mapFailed(string reason)
    {
        stat_map_failures.Value++;
        Logger.Warning($"[VulkanHardwareVideoTexture] Zero-copy map failed: {reason}.");
    }
}
