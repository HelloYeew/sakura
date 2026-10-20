// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.IO;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Statistic;
using Sakura.Framework.Timing;
using Vortice.Vulkan;
using SakuraVertex = Sakura.Framework.Graphics.Rendering.Vertex.Vertex;

namespace Sakura.Framework.Graphics.Rendering.Vulkan;

/// <summary>
/// Renders through Vulkan.
/// </summary>
/// <remarks>
/// <para>
/// Each frame is one command buffer and one queue submission. <see cref="StartFrame"/> waits for the
/// frame slot's previous use to finish, frees what was retired into it, runs the scheduled draw-thread
/// work, acquires a swapchain image and opens the render pass; <see cref="Draw"/> records the scene,
/// submits, and presents.
/// </para>
/// <para>
/// The renderer doesn't draw geometry yet: frames are cleared and presented, and textures, shaders and
/// framebuffers are CPU-side placeholders that keep the scene graph and the texture managers working.
/// </para>
/// </remarks>
public sealed unsafe class VulkanRenderer : ICustomShaderRenderer, IDisposable
{
    private static readonly GlobalStatistic<int> stat_queue_submits = GlobalStatistics.Get<int>("Vulkan", "Queue Submits", StatisticKind.PerFrame);
    private static readonly GlobalStatistic<int> stat_swapchain_rebuilds = GlobalStatistics.Get<int>("Vulkan", "Swapchain Rebuilds", StatisticKind.Cumulative);
    private static readonly GlobalStatistic<long> stat_frames_without_present = GlobalStatistics.Get<long>("Vulkan", "Frames Without Present", StatisticKind.Cumulative);

    /// <summary>
    /// The frame slots allocated. Up to two run at once: one while presentation is synced to the display
    /// (the CPU starts a frame only once the previous one has finished), two when uncapped.
    /// </summary>
    private const int max_frames_in_flight = 2;

    /// <summary>
    /// How long a wait on the GPU or the swapchain may block before it is reported. Waits never give up,
    /// since a frame slot can't be reused while its work may still be running; the report is how a hung
    /// GPU shows up.
    /// </summary>
    private const ulong wait_report_nanoseconds = 1_000_000_000;

    /// <summary>
    /// How long acquiring an image may block. A hidden window can stop the presentation engine from
    /// releasing images at all (Wayland under FIFO), and the frame is then skipped instead.
    /// </summary>
    private const ulong acquire_timeout_nanoseconds = 100_000_000;

    private static readonly VkClearColorValue clear_color = new VkClearColorValue(0f, 0f, 0f, 1f);

    private IVulkanGraphicsSurface? windowSurface;
    private VulkanDevice? device;
    private VulkanRenderPasses? renderPasses;
    private VulkanSwapchain? swapchain;
    private readonly VulkanFrameContext?[] frames = new VulkanFrameContext?[max_frames_in_flight];

    private int framesInFlight = 1;
    private int slot;

    /// <summary>
    /// The host's latest vsync request. It can arrive on any thread, so it is only applied at the start
    /// of a frame on the draw thread.
    /// </summary>
    private volatile bool vsyncRequested = true;

    private bool vsyncApplied = true;

    private VulkanFrameContext? currentFrame;
    private uint? currentImage;
    private bool renderPassOpen;

    private int drawableWidth;
    private int drawableHeight;

    /// <summary>
    /// The drawable size the swapchain was last built for. Compared with the window's drawable size
    /// rather than with the swapchain's extent, which a surface may legitimately report differently
    /// (fractional scaling), so a mismatch there can't turn into a rebuild every frame.
    /// </summary>
    private (int Width, int Height) swapchainBuiltFor;

    private DrawNode? rootNode;

    private readonly ConcurrentQueue<Action> drawThreadQueue = new ConcurrentQueue<Action>();
    private readonly TextureUploadQueue textureUploadQueue = new TextureUploadQueue();

    private readonly ClipStack clips = new ClipStack();

    private Matrix4x4 projectionMatrix = Matrix4x4.Identity;
    private float renderScaleX = 1f;
    private float renderScaleY = 1f;

    private double blockedMilliseconds;
    private bool waitReported;

    public Texture WhitePixel { get; } = new Texture(new HeadlessNativeTexture(1, 1), TextureOwnership.Shared);

    public Matrix4x4 ProjectionMatrix => projectionMatrix;

    public Vector2 RenderScale => new Vector2(renderScaleX, renderScaleY);

    public Storage ShaderStorage { get; set; } = null!;

    public DiskCache ShaderCache { get; set; } = null!;

    /// <inheritdoc cref="VulkanDevice.IsSupported"/>
    public static bool IsSupported() => VulkanDevice.IsSupported();

    public void Initialize(IGraphicsSurface graphicsSurface)
    {
        if (graphicsSurface is not IVulkanGraphicsSurface vulkanSurface)
            throw new InvalidOperationException($"{nameof(VulkanRenderer)} requires an {nameof(IVulkanGraphicsSurface)}.");

        windowSurface = vulkanSurface;
        device = new VulkanDevice(vulkanSurface, out var surface);
        renderPasses = new VulkanRenderPasses(device);
        swapchain = new VulkanSwapchain(device, vulkanSurface, renderPasses, surface);

        for (int i = 0; i < frames.Length; i++)
            frames[i] = new VulkanFrameContext(device, i);

        vulkanSurface.GetDrawableSize(out drawableWidth, out drawableHeight);
    }

    public void Clear()
    {
        // Folded into StartFrame: the render pass clears the image when it opens.
    }

    /// <inheritdoc/>
    public double LastBlockedMilliseconds => blockedMilliseconds;

    public void SetVSync(bool enabled) => vsyncRequested = enabled;

    public void StartFrame()
    {
        blockedMilliseconds = 0;
        currentFrame = null;
        currentImage = null;

        if (device == null || swapchain == null)
            return;

        applyVSync();

        var frame = frames[slot]!;

        waitForFence(frame.Fence);

        // The fence proves the GPU finished this slot's last frame, so what was retired during it can
        // go, and its commands can be reset.
        frame.ReleaseRetired();
        VulkanDevice.Check(device.Api.vkResetCommandPool(frame.CommandPool, VkCommandPoolResetFlags.None), "vkResetCommandPool");

        NativeDisposalQueue.Process();

        while (drawThreadQueue.TryDequeue(out var action))
            action();

        textureUploadQueue.Process();

        var beginInfo = new VkCommandBufferBeginInfo { flags = VkCommandBufferUsageFlags.OneTimeSubmit };
        VulkanDevice.Check(device.Api.vkBeginCommandBuffer(frame.CommandBuffer, &beginInfo), "vkBeginCommandBuffer");

        currentFrame = frame;
        currentImage = acquireImage(frame);

        clips.Reset();

        if (currentImage is uint image)
            beginSwapchainPass(frame, image);
    }

    public void SetRoot(DrawNode rootDrawNode) => rootNode = rootDrawNode;

    public void Resize(int physicalWidth, int physicalHeight, int logicalWidth, int logicalHeight)
    {
        drawableWidth = physicalWidth;
        drawableHeight = physicalHeight;

        renderScaleX = logicalWidth > 0 ? (float)physicalWidth / logicalWidth : 1f;
        renderScaleY = logicalHeight > 0 ? (float)physicalHeight / logicalHeight : 1f;

        // Vulkan's clip space points +Y down, so this "bottom-up" ortho puts the origin at the top-left,
        // exactly as it does on Metal, where the shader flips Y instead.
        projectionMatrix = Matrix4x4.CreateOrthographicOffCenter(0, logicalWidth, 0, logicalHeight, -1, 1);
    }

    public void Draw(IClock clock)
    {
        if (device == null || swapchain == null || currentFrame == null)
            return;

        stat_queue_submits.CompleteFrame();

        var frame = currentFrame;

        // A frame with no image (minimised, or the swapchain is being rebuilt) still submits, so work
        // recorded outside the render pass is never lost, but it draws nothing.
        if (currentImage != null)
            rootNode?.Draw(this);

        TextureBindTracker.EndFrame();

        if (renderPassOpen)
        {
            device.Api.vkCmdEndRenderPass(frame.CommandBuffer);
            renderPassOpen = false;
        }

        VulkanDevice.Check(device.Api.vkEndCommandBuffer(frame.CommandBuffer), "vkEndCommandBuffer");

        submit(frame);

        if (currentImage is uint image)
            present(image);
        else
            stat_frames_without_present.Value++;

        slot = (slot + 1) % framesInFlight;
        currentFrame = null;
    }

    #region Frame

    /// <summary>
    /// Switches between one frame in flight under vsync (input reaches the screen a frame sooner) and two
    /// when uncapped (the CPU records the next frame while the GPU draws this one), and rebuilds the
    /// swapchain for the matching present mode.
    /// </summary>
    private void applyVSync()
    {
        bool vsync = vsyncRequested;

        if (vsync == vsyncApplied)
            return;

        vsyncApplied = vsync;

        int wanted = vsync ? 1 : max_frames_in_flight;

        if (wanted != framesInFlight)
        {
            // Slots are about to be reused in a different rotation, so none may still be running.
            waitForAllFrames();
            framesInFlight = wanted;
            slot = 0;
        }

        swapchain!.SetVSync(vsync);
    }

    private void waitForAllFrames()
    {
        for (int i = 0; i < frames.Length; i++)
            waitForFence(frames[i]!.Fence);
    }

    /// <summary>
    /// Waits for <paramref name="fence"/>, counting the time as blocked on the device.
    /// </summary>
    private void waitForFence(VkFence fence)
    {
        long start = Stopwatch.GetTimestamp();

        while (true)
        {
            var result = device!.Api.vkWaitForFences(1, &fence, true, wait_report_nanoseconds);

            if (result == VkResult.Success)
                break;

            if (result != VkResult.Timeout)
                throw new InvalidOperationException($"vkWaitForFences failed: {result}. The GPU has stopped responding, and the renderer can't recover from that.");

            reportLongWait("waiting for the GPU to finish a frame");
        }

        blockedMilliseconds += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        waitReported = false;
    }

    private void reportLongWait(string what)
    {
        if (waitReported)
            return;

        waitReported = true;
        Logger.Warning($"Vulkan: {what} has taken over {wait_report_nanoseconds / 1_000_000} ms.");
    }

    /// <summary>
    /// Acquires the image this frame renders into, rebuilding the swapchain first when it needs it.
    /// </summary>
    /// <returns>The image index, or null when there is nothing to present to this frame.</returns>
    private uint? acquireImage(VulkanFrameContext frame)
    {
        var chain = swapchain!;

        // Some platforms resize the window without ever reporting the swapchain out of date.
        if (swapchainBuiltFor != (drawableWidth, drawableHeight))
            chain.Invalidate();

        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (chain.NeedsRecreate && !rebuildSwapchain())
                return null;

            long start = Stopwatch.GetTimestamp();
            var result = chain.Acquire(frame.ImageAvailable, acquire_timeout_nanoseconds, out uint image);
            blockedMilliseconds += Stopwatch.GetElapsedTime(start).TotalMilliseconds;

            switch (result)
            {
                case VkResult.Success:
                    return image;

                // Still usable, and the semaphore will be signalled, so this frame goes ahead. The present
                // result asks for the rebuild.
                case VkResult.SuboptimalKHR:
                    chain.Invalidate();
                    return image;

                case VkResult.ErrorOutOfDateKHR:
                    chain.Invalidate();
                    continue;

                case VkResult.Timeout:
                case VkResult.NotReady:
                    reportLongWait("acquiring a swapchain image");
                    return null;

                case VkResult.ErrorSurfaceLostKHR:
                    replaceSurface();
                    continue;

                default:
                    VulkanDevice.Check(result, "vkAcquireNextImageKHR");
                    return null;
            }
        }

        return null;
    }

    /// <returns>False when the window has no area to present to.</returns>
    private bool rebuildSwapchain()
    {
        if (!swapchain!.Recreate())
            return false;

        swapchainBuiltFor = (drawableWidth, drawableHeight);
        stat_swapchain_rebuilds.Value++;
        return true;
    }

    private void replaceSurface()
    {
        Logger.Verbose("Vulkan: the window surface was lost; creating a new one.");
        swapchain!.RecreateSurface();
    }

    private void beginSwapchainPass(VulkanFrameContext frame, uint image)
    {
        var chain = swapchain!;
        var clearValue = new VkClearValue { color = clear_color };

        var beginInfo = new VkRenderPassBeginInfo
        {
            renderPass = chain.RenderPass,
            framebuffer = chain.GetFramebuffer(image),
            renderArea = new VkRect2D(0, 0, chain.Extent.width, chain.Extent.height),
            clearValueCount = 1,
            pClearValues = &clearValue,
        };

        device!.Api.vkCmdBeginRenderPass(frame.CommandBuffer, &beginInfo, VkSubpassContents.Inline);
        renderPassOpen = true;
    }

    private void submit(VulkanFrameContext frame)
    {
        var api = device!.Api;

        var commandBuffer = frame.CommandBuffer;
        var waitSemaphore = frame.ImageAvailable;
        var waitStage = VkPipelineStageFlags.ColorAttachmentOutput;
        var signalSemaphore = currentImage is uint image ? swapchain!.GetRenderFinished(image) : VkSemaphore.Null;

        var submitInfo = new VkSubmitInfo
        {
            // Only the first write to the image has to wait for it to be available, so earlier work
            // (uploads) runs while presentation still holds it.
            waitSemaphoreCount = currentImage != null ? 1u : 0u,
            pWaitSemaphores = &waitSemaphore,
            pWaitDstStageMask = &waitStage,
            commandBufferCount = 1,
            pCommandBuffers = &commandBuffer,
            signalSemaphoreCount = currentImage != null ? 1u : 0u,
            pSignalSemaphores = &signalSemaphore,
        };

        var fence = frame.Fence;

        // Reset only now, right before the submit that signals it again, so nothing between the wait in
        // StartFrame and here can leave the slot with a fence that never signals.
        VulkanDevice.Check(api.vkResetFences(1, &fence), "vkResetFences");
        VulkanDevice.Check(api.vkQueueSubmit(device.GraphicsQueue, 1, &submitInfo, fence), "vkQueueSubmit");

        stat_queue_submits.Accumulator++;
    }

    private void present(uint image)
    {
        long start = Stopwatch.GetTimestamp();
        var result = swapchain!.Present(image);
        blockedMilliseconds += Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        switch (result)
        {
            case VkResult.Success:
                break;

            case VkResult.SuboptimalKHR:
            case VkResult.ErrorOutOfDateKHR:
                swapchain.Invalidate();
                break;

            case VkResult.ErrorSurfaceLostKHR:
                replaceSurface();
                break;

            default:
                VulkanDevice.Check(result, "vkQueuePresentKHR");
                break;
        }
    }

    #endregion

    #region Drawing

    public void DrawVertices(ReadOnlySpan<SakuraVertex> vertices, Texture texture)
    {
    }

    public void DrawQuads(ReadOnlySpan<SakuraVertex> vertices, Texture texture)
    {
    }

    public void DrawVerticesRaw(ReadOnlySpan<SakuraVertex> vertices)
    {
    }

    public void DrawEdgeEffect(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius, float edgeRadius, Vector2 offset, Color color, bool glow, bool hollow, ReadOnlySpan<SakuraVertex> quadVertices)
    {
    }

    public void FlushBatch()
    {
    }

    public void SetBlendMode(BlendingMode blendingMode)
    {
    }

    public void RestoreMainShader()
    {
    }

    public void ApplyCurrentClip(Span<SakuraVertex> vertices) => clips.ApplyTo(vertices);

    public void PushMask(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius)
        => clips.Push(maskCenter, maskHalfSize, shearX, cornerRadius);

    public void PopMask(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius, float borderThickness, Color borderColor, ReadOnlySpan<SakuraVertex> maskVertices = default)
        => clips.Pop();

    public void BindFrameBuffer(IFrameBuffer frameBuffer, RectangleF sourceRect, Color clearColor = default)
    {
    }

    public void UnbindFrameBuffer()
    {
    }

    #endregion

    #region Resources

    public void ScheduleToDrawThread(Action action) => drawThreadQueue.Enqueue(action);

    public void ScheduleTextureUpload(Action upload, long approximateBytes) => textureUploadQueue.Enqueue(upload, approximateBytes);

    public INativeTexture CreateNativeTexture(int width, int height) => new HeadlessNativeTexture(width, height);

    public IShader CreateShader(Storage storage, string vertexPath, string fragmentPath) => new HeadlessShader();

    public INativeVideoTexture CreateVideoTexture(int width, int height, VideoPlaneLayout layout, bool fromHardwareFrame = false)
        => new HeadlessNativeVideoTexture(width, height, layout);

    public bool CanSampleHardwareFrame(FFmpeg.AutoGen.AVFrame* frame) => false;

    public IFrameBuffer CreateFrameBuffer(int width, int height, bool pixelSnapping = false) => new PlaceholderFrameBuffer(width, height);

    /// <summary>
    /// Stands in for an offscreen target until this renderer has real ones.
    /// </summary>
    private sealed class PlaceholderFrameBuffer : IFrameBuffer
    {
        public Texture Texture { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }

        public PlaceholderFrameBuffer(int width, int height)
        {
            Width = Math.Max(1, width);
            Height = Math.Max(1, height);
            Texture = new Texture(new HeadlessNativeTexture(Width, Height));
        }

        public void Resize(int width, int height)
        {
            if (Math.Max(1, width) == Width && Math.Max(1, height) == Height)
                return;

            Texture.Dispose();

            Width = Math.Max(1, width);
            Height = Math.Max(1, height);
            Texture = new Texture(new HeadlessNativeTexture(Width, Height));
        }

        public void Dispose() => Texture.Dispose();
    }

    #endregion

    public void Dispose()
    {
        if (device == null)
            return;

        device.WaitIdle();

        foreach (var frame in frames)
            frame?.Dispose();

        swapchain?.Dispose();
        renderPasses?.Dispose();
        device.Dispose();

        device = null;
        swapchain = null;
        renderPasses = null;
        windowSurface = null;
    }
}
