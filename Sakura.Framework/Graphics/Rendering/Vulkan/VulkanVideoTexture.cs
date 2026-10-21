// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Threading;
using FFmpeg.AutoGen;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Statistic;
using Vortice.Vulkan;

namespace Sakura.Framework.Graphics.Rendering.Vulkan;

/// <summary>
/// The Vulkan plane set for one video frame: three <c>R8_UNORM</c> images (Y, U, V) under
/// <see cref="VideoPlaneLayout.Yuv420P"/>, or an <c>R8_UNORM</c> luma plane and a half-resolution
/// <c>R8G8_UNORM</c> plane holding Cb and Cr interleaved under <see cref="VideoPlaneLayout.Nv12"/>.
/// UNORM rather than sRGB: the samples are raw luma and chroma, which the video shader converts, transfer
/// function included.
/// </summary>
/// <remarks>
/// A frame is copied in through staging memory along with the frame's other uploads, so it is in place
/// before any of that frame's draws. A plane that frames still in flight are sampling is only
/// overwritten once they are done with it: the copy waits behind a barrier on their fragment shaders.
/// </remarks>
public sealed unsafe class VulkanVideoTexture : INativeVideoTexture
{
    private readonly VulkanRenderer renderer;
    private readonly VulkanTexture[] planes;
    private readonly NativeMemoryLease memoryLease;

    public int Width { get; }
    public int Height { get; }

    public VideoPlaneLayout Layout { get; }

    /// <summary>
    /// Whether a frame has been uploaded and is ready to draw. Written on the draw thread, read on the
    /// update thread.
    /// </summary>
    public bool Available => Volatile.Read(ref available);

    private bool available;

    /// <summary>
    /// 1.5 bytes per pixel across the plane set, the same figure this texture's memory lease is taken
    /// for, so the tracker and anything displaying a size agree.
    /// </summary>
    public long PlaneBytes => NativeTextureMemory.BytesForVideoPlanes(Width, Height);

    public TextureBindCounter Binds { get; } = new TextureBindCounter();

    private bool disposed;

    internal VulkanVideoTexture(VulkanRenderer renderer, VulkanDevice device, VulkanMemory memory, VkSampler sampler, int width, int height, VideoPlaneLayout layout)
    {
        this.renderer = renderer;
        Width = width;
        Height = height;
        Layout = layout;

        // Chroma is half-resolution (rounded up) in both layouts; they differ in how many images hold it,
        // not in how much of it there is.
        int chromaWidth = (width + 1) / 2;
        int chromaHeight = (height + 1) / 2;

        planes = new VulkanTexture[layout.PlaneCount()];

        try
        {
            planes[0] = VulkanTexture.CreateVideoPlane(renderer, device, memory, width, height, sampler, VkFormat.R8Unorm);

            if (layout == VideoPlaneLayout.Nv12)
                planes[1] = VulkanTexture.CreateVideoPlane(renderer, device, memory, chromaWidth, chromaHeight, sampler, VkFormat.R8G8Unorm);
            else
            {
                planes[1] = VulkanTexture.CreateVideoPlane(renderer, device, memory, chromaWidth, chromaHeight, sampler, VkFormat.R8Unorm);
                planes[2] = VulkanTexture.CreateVideoPlane(renderer, device, memory, chromaWidth, chromaHeight, sampler, VkFormat.R8Unorm);
            }
        }
        catch
        {
            foreach (var plane in planes)
                plane?.Dispose();

            throw;
        }

        // Taken once every plane exists, so a failed creation leaves nothing on the books.
        memoryLease = NativeMemoryTracker.Add(NativeMemoryCategory.Video, NativeTextureMemory.BytesForVideoPlanes(width, height));
    }

    /// <summary>
    /// Puts the planes at texture slots 0, 1 (and 2 for YUV420P), the video shaders' bindings in set 1.
    /// <paramref name="tiling"/> samples them with a repeating wrap (tiled fill) rather than clamped to
    /// the edge (the normal video case, which avoids bleeding the opposite edge in). Draw thread only.
    /// </summary>
    public void BindPlanes(bool tiling)
    {
        Binds.Record();

        for (int i = 0; i < planes.Length; i++)
            renderer.BindVideoPlane(i, planes[i], tiling);
    }

    /// <summary>
    /// Copies a decoded frame into the planes, each read with its own FFmpeg line size as the row stride.
    /// Draw thread only.
    /// </summary>
    public void Upload(AVFrame* frame)
    {
        int width = frame->width;
        int height = frame->height;
        int chromaWidth = (width + 1) / 2;
        int chromaHeight = (height + 1) / 2;

        planes[0].UploadPlane(frame->data[0], frame->linesize[0], width, height);
        planes[1].UploadPlane(frame->data[1], frame->linesize[1], chromaWidth, chromaHeight);

        if (Layout == VideoPlaneLayout.Yuv420P)
            planes[2].UploadPlane(frame->data[2], frame->linesize[2], chromaWidth, chromaHeight);

        MarkAvailable();
    }

    public void MarkAvailable() => Volatile.Write(ref available, true);

    /// <summary>
    /// Releases the planes once the GPU has finished every frame that may sample them. Draw thread only.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        Volatile.Write(ref available, false);

        foreach (var plane in planes)
            plane.Dispose();

        memoryLease.Dispose();
    }
}
