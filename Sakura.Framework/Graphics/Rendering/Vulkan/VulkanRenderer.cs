// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Sakura.Framework.Development;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Rendering.Batches;
using Sakura.Framework.Graphics.Rendering.Uniforms;
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
/// Each frame is one queue submission of up to two command buffers. <see cref="StartFrame"/> waits for
/// the frame slot's previous use to finish, frees what was retired into it, runs the scheduled
/// draw-thread work (texture uploads among it), acquires a swapchain image and opens the render pass;
/// <see cref="Draw"/> records the scene, submits, and presents. Texture uploads go into a separate
/// command buffer that runs first, so they never interrupt the render pass, wherever in the frame they
/// are recorded.
/// </para>
/// <para>
/// Geometry is batched as on Direct3D 11: indexed quads drawn with a static index buffer, one draw per
/// flush. Uniform blocks are written to a fresh offset of the frame's stream buffer whenever they change
/// and reach the shader as dynamic offsets into one descriptor set; the textures a batch samples are a
/// 16-entry table that becomes a second descriptor set whenever it changes.
/// </para>
/// <para>
/// Binding a framebuffer ends the current render pass and opens one on the framebuffer; unbinding ends
/// it and marks the parent target to be reopened (with a pass that keeps its contents) the next time
/// something is drawn into it, so a chain of offscreen passes doesn't open and close empty ones in
/// between.
/// </para>
/// <para>
/// Video frames are copied into plane images like any other upload (<see cref="VulkanVideoTexture"/>);
/// none is sampled straight out of the decoder's memory yet.
/// </para>
/// </remarks>
public sealed unsafe class VulkanRenderer : ICustomShaderRenderer, IDisposable
{
    private static readonly GlobalStatistic<int> stat_draw_calls = GlobalStatistics.Get<int>("Renderer", "Draw Calls", StatisticKind.PerFrame);
    private static readonly GlobalStatistic<int> stat_vertices_drawn = GlobalStatistics.Get<int>("Renderer", "Vertices Drawn", StatisticKind.PerFrame);
    private static readonly GlobalStatistic<int> stat_slot_exhaustion_flushes = GlobalStatistics.Get<int>("Renderer", "Slot Exhaustion Flushes", StatisticKind.PerFrame);
    private static readonly GlobalStatistic<int> stat_state_change_flushes = GlobalStatistics.Get<int>("Renderer", "State Change Flushes", StatisticKind.PerFrame);

    private static readonly GlobalStatistic<int> stat_queue_submits = GlobalStatistics.Get<int>("Vulkan", "Queue Submits", StatisticKind.PerFrame);
    private static readonly GlobalStatistic<int> stat_render_passes = GlobalStatistics.Get<int>("Vulkan", "Render Passes", StatisticKind.PerFrame);
    private static readonly GlobalStatistic<int> stat_descriptor_sets = GlobalStatistics.Get<int>("Vulkan", "Descriptor Sets Allocated", StatisticKind.PerFrame);
    private static readonly GlobalStatistic<long> stat_staging_bytes = GlobalStatistics.Get<long>("Vulkan", "Staging Bytes", StatisticKind.PerFrame, StatisticUnit.Bytes);
    private static readonly GlobalStatistic<int> stat_retire_queue = GlobalStatistics.Get<int>("Vulkan", "Retire Queue Depth");
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

    /// <summary>
    /// Vertex capacity of a batch, as on GL and Direct3D 11: 3000 quads before a capacity flush.
    /// </summary>
    private const int max_batch_vertices = 1000 * 12;

    private const int texture_slot_count = (int)VulkanShaderLayout.TEXTURE_ARRAY_LENGTH;

    /// <summary>
    /// Uploads larger than this get a staging buffer of their own rather than space in the frame's
    /// stream buffer, which would otherwise grow to hold the largest image ever uploaded.
    /// </summary>
    private const ulong dedicated_staging_threshold = 512 * 1024;

    /// <summary>
    /// How many texture tables written this frame are remembered for reuse. Drawing alternates between
    /// a few tables far more often than it builds new ones.
    /// </summary>
    private const int texture_set_cache_size = 8;

    private static readonly VkClearColorValue clear_color = new VkClearColorValue(0f, 0f, 0f, 1f);

    private IVulkanGraphicsSurface? windowSurface;
    private VulkanDevice? device;
    private VulkanMemory? memory;
    private VulkanRenderPasses? renderPasses;
    private VulkanDescriptors? descriptors;
    private VulkanPipelines? pipelines;
    private VulkanSwapchain? swapchain;
    private VulkanStagingPool? stagingPool;
    private readonly VulkanFrameContext?[] frames = new VulkanFrameContext?[max_frames_in_flight];

    private int framesInFlight = 1;
    private int slot;

    /// <summary>
    /// The slot of the frame submitted last, which objects released between frames are retired into:
    /// it finishes after every earlier frame, so its fence covers anything that could still use them.
    /// </summary>
    private int lastSubmittedSlot;

    /// <summary>
    /// The host's latest vsync request. It can arrive on any thread, so it is only applied at the start
    /// of a frame on the draw thread.
    /// </summary>
    private volatile bool vsyncRequested = true;

    private bool vsyncApplied = true;

    private VulkanFrameContext? currentFrame;
    private uint? currentImage;
    private bool renderPassOpen;

    /// <summary>
    /// The framebuffer draws go into, or null for the swapchain image.
    /// </summary>
    private VulkanFrameBuffer? currentTarget;

    /// <summary>
    /// Whether <see cref="currentTarget"/>'s pass was closed by a nested framebuffer and has to be
    /// reopened before anything more is drawn into it.
    /// </summary>
    private bool targetNeedsReopen;

    /// <summary>
    /// The attachment format of the pass being recorded, which pipelines are chosen by.
    /// </summary>
    private VkFormat currentPassFormat;

    private readonly Stack<TargetState> targetStack = new Stack<TargetState>();

    private readonly record struct TargetState(VulkanFrameBuffer? FrameBuffer, Matrix4x4 Projection, ClipState Clip);

    private VkFormat? offscreenFormat;

    /// <summary>
    /// What frames render into when the renderer has no window (<see cref="InitializeOffscreen"/>).
    /// </summary>
    private VulkanFrameBuffer? offscreenScreen;

    /// <summary>
    /// The linear color the offscreen target is cleared to at the start of each frame.
    /// </summary>
    internal Vector4 OffscreenClearColor { get; set; } = new Vector4(0, 0, 0, 1);

    /// <summary>
    /// Whether a frame has been started and not yet submitted.
    /// </summary>
    internal bool IsFrameOpen => currentFrame != null;

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

    private VertexBatch? batch;
    private VkBuffer quadIndexBuffer;
    private VulkanAllocation quadIndexAllocation;

    private VulkanShader? mainShader;
    private VulkanShader? currentShader;
    private BlendingMode currentBlendMode = BlendingMode.Alpha;
    private MaskBlock maskState;

    private VkPipeline boundPipeline;
    private bool pipelineDirty = true;

    /// <summary>
    /// The latest contents of every uniform block, so they can be written again whenever the offsets
    /// they were written at stop being valid: each frame, and whenever the stream buffer is replaced.
    /// </summary>
    private readonly byte[] uniformShadow = new byte[VulkanShaderLayout.UNIFORM_BINDING_COUNT * VulkanShaderLayout.UNIFORM_BLOCK_RANGE];

    private readonly bool[] uniformWritten = new bool[VulkanShaderLayout.UNIFORM_BINDING_COUNT];
    private readonly uint[] uniformOffsets = new uint[VulkanShaderLayout.UNIFORM_BINDING_COUNT];
    private VkDescriptorSet uniformSet;
    private VkBuffer uniformSetBuffer;
    private bool uniformsDirty = true;

    /// <summary>
    /// The texture table the next flush samples, with the CPU-side handle of each entry for lookups.
    /// Entries a batch hasn't assigned are bound to the white pixel.
    /// </summary>
    private readonly VkImageView[] slotViews = new VkImageView[texture_slot_count];

    private readonly VkSampler[] slotSamplers = new VkSampler[texture_slot_count];
    private readonly nint[] slotHandles = new nint[texture_slot_count];
    private int slotCount;
    private bool textureSetDirty = true;

    private readonly VkImageView[] setViews = new VkImageView[texture_slot_count];
    private readonly VkSampler[] setSamplers = new VkSampler[texture_slot_count];
    private readonly VkImageView[] cachedSetViews = new VkImageView[texture_set_cache_size * texture_slot_count];
    private readonly VkSampler[] cachedSetSamplers = new VkSampler[texture_set_cache_size * texture_slot_count];
    private readonly VkDescriptorSet[] cachedSets = new VkDescriptorSet[texture_set_cache_size];
    private readonly bool[] cachedSetIsArray = new bool[texture_set_cache_size];
    private int cachedSetCount;
    private int cachedSetNext;

    /// <summary>
    /// Textures copied into this frame that still need their mip chain and their transition back to
    /// being sampled, both recorded once at the end of the frame.
    /// </summary>
    private readonly List<VulkanTexture> texturesAwaitingFinish = new List<VulkanTexture>();

    private VkCommandPool immediateCommandPool;

    private ulong uniformAlignment;
    private ulong stagingAlignment;
    private bool mipmapsSupported;

    private VulkanTexture? whiteTexture;

    /// <summary>
    /// Every texture, framebuffer and shader created on the device and not yet disposed. Whatever their
    /// owners never dispose (a buffered container's framebuffer, the effect shaders kept in statics) is
    /// released here when the renderer is, since the device can only be destroyed once nothing created on
    /// it is left.
    /// </summary>
    private readonly HashSet<IDisposable> liveResources = new HashSet<IDisposable>();

    private readonly System.Threading.Lock liveResourcesLock = new System.Threading.Lock();

    /// <summary>
    /// The 1x1 white texture sampled by solid-color geometry and in place of textures not uploaded yet.
    /// </summary>
    internal VulkanTexture? WhiteTexture => whiteTexture;

    public Texture WhitePixel { get; private set; } = null!;

    public Matrix4x4 ProjectionMatrix => projectionMatrix;

    public Vector2 RenderScale => new Vector2(renderScaleX, renderScaleY);

    public Storage ShaderStorage { get; set; } = null!;

    public DiskCache ShaderCache { get; set; } = null!;

    /// <inheritdoc cref="VulkanDevice.IsSupported"/>
    public static bool IsSupported() => VulkanDevice.IsSupported();

    /// <inheritdoc cref="VulkanDevice.IsSupportedOffscreen"/>
    internal static bool IsSupportedOffscreen() => VulkanDevice.IsSupportedOffscreen();

    public void Initialize(IGraphicsSurface graphicsSurface)
    {
        if (graphicsSurface is not IVulkanGraphicsSurface vulkanSurface)
            throw new InvalidOperationException($"{nameof(VulkanRenderer)} requires an {nameof(IVulkanGraphicsSurface)}.");

        windowSurface = vulkanSurface;
        createDevice(vulkanSurface);

        vulkanSurface.GetDrawableSize(out drawableWidth, out drawableHeight);
    }

    /// <summary>
    /// Initialises the renderer with no window: every frame renders into an offscreen target of the given
    /// size, which <see cref="ReadOffscreen"/> reads back. Used by tests, so they can render on any machine
    /// with a Vulkan driver, a software one included.
    /// </summary>
    internal void InitializeOffscreen(int width, int height)
    {
        createDevice(null);

        offscreenScreen = new VulkanFrameBuffer(this, device!, memory!, renderPasses!, chooseOffscreenFormat(), descriptors!.RenderTargetSampler, width, height);

        foreach (var blend in Enum.GetValues<BlendingMode>())
            pipelines!.Get(mainShader!, blend, offscreenScreen.Format);

        Resize(width, height, width, height);
    }

    private void createDevice(IVulkanGraphicsSurface? vulkanSurface)
    {
        device = new VulkanDevice(vulkanSurface, out var surface);
        memory = new VulkanMemory(device);
        renderPasses = new VulkanRenderPasses(device);
        descriptors = new VulkanDescriptors(device);
        pipelines = new VulkanPipelines(device, renderPasses, ShaderCache);

        if (vulkanSurface != null)
            swapchain = new VulkanSwapchain(device, vulkanSurface, renderPasses, surface);

        var limits = device.Properties.limits;
        uniformAlignment = Math.Max(limits.minUniformBufferOffsetAlignment, 16);
        // Copies from a buffer into a color image need the offset to be a multiple of the texel size.
        stagingAlignment = Math.Max(limits.optimalBufferCopyOffsetAlignment, 4);
        mipmapsSupported = supportsMipmapGeneration(device);

        stagingPool = new VulkanStagingPool(device, memory);

        for (int i = 0; i < frames.Length; i++)
            frames[i] = new VulkanFrameContext(device, memory, stagingPool, i);

        createImmediateCommandPool();
        createQuadIndexBuffer();
        createWhitePixel();

        mainShader = new VulkanShader(this, device, ShaderCompiler.GetOrCompileSpirv(ShaderStorage, "shader.vert", "shader.frag", ShaderCache, DebugUtils.IsDebugBuild), "shader.vert+shader.frag", descriptors);
        currentShader = mainShader;

        batch = new VertexBatch(max_batch_vertices, FlushBatch);

        if (!mipmapsSupported)
            Logger.Warning($"Vulkan: {VulkanTexture.FORMAT} can't be blitted with linear filtering here, so textures have no mip levels and minify with more aliasing.");
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

        if (device == null || (swapchain == null && offscreenScreen == null))
            return;

        applyVSync();

        var frame = frames[slot]!;

        waitForFence(frame.Fence);

        // The fence proves the GPU finished this slot's last frame: what was retired during it can go,
        // and its command buffers, descriptor sets, stream buffer and staging buffers can be reused.
        frame.Reset();
        stagingPool!.Trim();

        currentFrame = frame;
        cachedSetCount = 0;
        cachedSetNext = 0;
        uniformSet = VkDescriptorSet.Null;

        // The stream buffer starts empty, so every block lives at an offset that no longer exists.
        clips.Reset();
        maskState = default;
        writeUniform(1, in maskState);
        rewriteUniforms();

        int retired = 0;
        foreach (var f in frames)
            retired += f!.RetiredCount;
        stat_retire_queue.Value = retired;

        // Uploads among this work record into the frame's upload command buffer.
        NativeDisposalQueue.Process();

        while (drawThreadQueue.TryDequeue(out var action))
            action();

        textureUploadQueue.Process();

        var beginInfo = new VkCommandBufferBeginInfo { flags = VkCommandBufferUsageFlags.OneTimeSubmit };
        VulkanDevice.Check(device.Api.vkBeginCommandBuffer(frame.CommandBuffer, &beginInfo), "vkBeginCommandBuffer");

        currentImage = swapchain != null ? acquireImage(frame) : null;

        currentShader = mainShader;
        currentBlendMode = BlendingMode.Alpha;
        currentTarget = null;
        targetNeedsReopen = false;
        targetStack.Clear();
        resetTextureSlotsToWhite();

        if (offscreenScreen is VulkanFrameBuffer screen)
        {
            currentTarget = screen;

            var clear = new VkClearColorValue(OffscreenClearColor.X, OffscreenClearColor.Y, OffscreenClearColor.Z, OffscreenClearColor.W);
            beginPass(renderPasses!.Get(screen.Format, VulkanPassKind.OffscreenClear), screen.Handle, screen.Format, (uint)screen.Width, (uint)screen.Height, clear);
            screen.ColorTexture.MarkRendered();
        }
        else if (currentImage is uint image)
            beginPass(swapchain!.RenderPass, swapchain.GetFramebuffer(image), swapchain.Format, swapchain.Extent.width, swapchain.Extent.height, clear_color);
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

        FlushBatch();
        var block = new ProjectionBlock { Projection = projectionMatrix };
        writeUniform(0, in block);
    }

    public void Draw(IClock clock)
    {
        if (device == null || currentFrame == null)
            return;

        stat_draw_calls.CompleteFrame();
        stat_vertices_drawn.CompleteFrame();
        stat_slot_exhaustion_flushes.CompleteFrame();
        stat_state_change_flushes.CompleteFrame();
        stat_queue_submits.CompleteFrame();
        stat_render_passes.CompleteFrame();
        stat_descriptor_sets.CompleteFrame();
        stat_staging_bytes.CompleteFrame();

        var frame = currentFrame;

        // A frame with no image (minimised, or the swapchain is being rebuilt) still submits its uploads,
        // but draws nothing.
        if (currentImage != null || offscreenScreen != null)
            rootNode?.Draw(this);

        FlushBatch();

        // After the final flush, so the batch's binds land in this frame's count.
        TextureBindTracker.EndFrame();

        // Whatever pass is open ends here: the swapchain's leaves the image ready to present, and an
        // offscreen one (a framebuffer left bound) its image ready to sample.
        endPass();

        if (frame.HasUploads)
        {
            finishUploads(frame.UploadCommandBuffer);
            VulkanDevice.Check(device.Api.vkEndCommandBuffer(frame.UploadCommandBuffer), "vkEndCommandBuffer");
        }

        VulkanDevice.Check(device.Api.vkEndCommandBuffer(frame.CommandBuffer), "vkEndCommandBuffer");

        frame.Stream.FlushWrites();
        submit(frame);

        if (currentImage is uint image)
            present(image);
        else if (swapchain != null)
            stat_frames_without_present.Value++;

        lastSubmittedSlot = slot;
        slot = (slot + 1) % framesInFlight;
        currentFrame = null;

        pipelines!.SaveIfChanged();
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

        swapchain?.SetVSync(vsync);
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

        // Compiled now rather than the first time each blend mode is drawn, which would be a hitch in
        // the middle of a frame. After the first run these come straight from the pipeline cache.
        foreach (var blend in Enum.GetValues<BlendingMode>())
            pipelines!.Get(mainShader!, blend, swapchain.Format);

        return true;
    }

    private void replaceSurface()
    {
        Logger.Verbose("Vulkan: the window surface was lost; creating a new one.");
        swapchain!.RecreateSurface();
    }

    /// <summary>
    /// Opens a render pass into <paramref name="framebuffer"/>, clearing it to <paramref name="clear"/>
    /// when the pass is a clearing one, and resets the state a draw has to bind again.
    /// </summary>
    private void beginPass(VkRenderPass renderPass, VkFramebuffer framebuffer, VkFormat format, uint width, uint height, VkClearColorValue clear)
    {
        var commandBuffer = currentFrame!.CommandBuffer;
        var clearValue = new VkClearValue { color = clear };

        var beginInfo = new VkRenderPassBeginInfo
        {
            renderPass = renderPass,
            framebuffer = framebuffer,
            renderArea = new VkRect2D(0, 0, width, height),
            clearValueCount = 1,
            pClearValues = &clearValue,
        };

        var api = device!.Api;
        api.vkCmdBeginRenderPass(commandBuffer, &beginInfo, VkSubpassContents.Inline);
        renderPassOpen = true;
        currentPassFormat = format;
        stat_render_passes.Accumulator++;

        var viewport = new VkViewport(0, 0, width, height, 0, 1);
        var scissor = new VkRect2D(0, 0, width, height);
        api.vkCmdSetViewport(commandBuffer, 0, 1, &viewport);
        api.vkCmdSetScissor(commandBuffer, 0, 1, &scissor);

        boundPipeline = VkPipeline.Null;
        pipelineDirty = true;
        uniformsDirty = true;
        textureSetDirty = true;
    }

    private void endPass()
    {
        if (!renderPassOpen)
            return;

        device!.Api.vkCmdEndRenderPass(currentFrame!.CommandBuffer);
        renderPassOpen = false;
    }

    /// <summary>
    /// Makes sure a pass into the current target is open before something is drawn, reopening it with
    /// its contents kept when a nested framebuffer closed it.
    /// </summary>
    /// <returns>False when there is nothing to draw into this frame (no swapchain image).</returns>
    private bool ensurePassOpen()
    {
        if (renderPassOpen)
            return true;

        if (!targetNeedsReopen)
            return false;

        targetNeedsReopen = false;

        if (currentTarget is VulkanFrameBuffer target)
        {
            beginPass(renderPasses!.Get(target.Format, VulkanPassKind.OffscreenLoad), target.Handle, target.Format, (uint)target.Width, (uint)target.Height, default);
            return true;
        }

        if (currentImage is not uint image)
            return false;

        var chain = swapchain!;
        beginPass(renderPasses!.Get(chain.Format, VulkanPassKind.SwapchainLoad), chain.GetFramebuffer(image), chain.Format, chain.Extent.width, chain.Extent.height, default);
        return true;
    }

    private void submit(VulkanFrameContext frame)
    {
        var api = device!.Api;

        // The upload buffer goes first: everything copied during the frame lands before any draw.
        var commandBuffers = stackalloc VkCommandBuffer[2];
        uint commandBufferCount = 0;

        if (frame.HasUploads)
            commandBuffers[commandBufferCount++] = frame.UploadCommandBuffer;

        commandBuffers[commandBufferCount++] = frame.CommandBuffer;

        var waitSemaphore = frame.ImageAvailable;
        var waitStage = VkPipelineStageFlags.ColorAttachmentOutput;
        var signalSemaphore = currentImage is uint image ? swapchain!.GetRenderFinished(image) : VkSemaphore.Null;

        var submitInfo = new VkSubmitInfo
        {
            // Only the first write to the image has to wait for it to be available, so the uploads run
            // while presentation still holds it.
            waitSemaphoreCount = currentImage != null ? 1u : 0u,
            pWaitSemaphores = &waitSemaphore,
            pWaitDstStageMask = &waitStage,
            commandBufferCount = commandBufferCount,
            pCommandBuffers = commandBuffers,
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

    /// <summary>
    /// Records a resource created on the device, so <see cref="Dispose"/> can release it if its owner
    /// never does. Thread-safe: textures are created off the draw thread.
    /// </summary>
    internal void Track(IDisposable resource)
    {
        lock (liveResourcesLock)
            liveResources.Add(resource);
    }

    internal void Untrack(IDisposable resource)
    {
        lock (liveResourcesLock)
            liveResources.Remove(resource);
    }

    /// <summary>
    /// Destroys <paramref name="destroy"/>'s object once no frame that could use it is still running on
    /// the GPU. Draw thread only.
    /// </summary>
    internal void Retire(Action destroy)
    {
        // While a frame is recorded it is the latest to reference anything; between frames, the last one
        // submitted is.
        var frame = currentFrame ?? frames[lastSubmittedSlot];

        if (frame == null)
        {
            destroy();
            return;
        }

        frame.Retire(destroy);
    }

    #endregion

    #region Drawing

    /// <summary>
    /// Resolves a texture to its index in the texture table, adding it when it isn't there yet and
    /// flushing when the table is full. Mirrors <c>GLRenderer.prepareTexture</c>.
    /// </summary>
    private float prepareTexture(Texture texture)
    {
        var native = texture?.BackendTexture as VulkanTexture;

        // A texture with no Vulkan backing (a placeholder framebuffer, a video frame drawn by its own
        // node) or one whose pixels haven't landed yet samples white. VulkanTexture.Bind applies the same
        // fallback, so it is resolved here first, or the slot would be keyed by a texture other than the
        // one bound to it.
        if (native == null || !native.Available || native.IsReleased)
            native = whiteTexture;

        if (native == null)
            return 0f;

        nint handle = native.Handle;

        for (int i = 0; i < slotCount; i++)
        {
            if (slotHandles[i] == handle)
                return i;
        }

        if (slotCount < texture_slot_count)
        {
            int newSlot = slotCount++;
            // VulkanTexture.Bind counts the bind itself, so every backend's figure comes from the same place.
            native.Bind(newSlot);
            return newSlot;
        }

        // Every entry is taken: draw what is batched and start the table over.
        stat_slot_exhaustion_flushes.Accumulator++;
        FlushBatch();
        resetTextureSlots();

        native.Bind(0);
        slotCount = 1;
        return 0;
    }

    /// <summary>
    /// Puts <paramref name="texture"/> at entry <paramref name="index"/> of the texture table the next
    /// flush samples. Called by <see cref="VulkanTexture.Bind"/>.
    /// </summary>
    internal void BindTexture(int index, VulkanTexture texture) => bindTexture(index, texture, texture.Sampler);

    private void bindTexture(int index, VulkanTexture texture, VkSampler sampler)
    {
        if (index < 0 || index >= texture_slot_count)
            return;

        slotViews[index] = texture.View;
        slotSamplers[index] = sampler;
        slotHandles[index] = texture.Handle;
        textureSetDirty = true;
    }

    /// <summary>
    /// Puts a video plane at entry <paramref name="index"/> of the texture table, sampled repeating when
    /// <paramref name="tiling"/> and clamped to the edge otherwise. A plane nothing has been uploaded into
    /// samples white, as any other texture would. Called by <see cref="VulkanVideoTexture.BindPlanes"/>.
    /// </summary>
    internal void BindVideoPlane(int index, VulkanTexture plane, bool tiling)
    {
        var bound = plane.Available && !plane.IsReleased ? plane : whiteTexture;

        if (bound == null)
            return;

        bindTexture(index, bound, tiling ? descriptors!.TextureSampler : descriptors!.RenderTargetSampler);
    }

    /// <summary>
    /// Drops a texture being released from the texture table, so a later texture can't be mistaken for
    /// it.
    /// </summary>
    internal void ForgetTexture(VulkanTexture texture)
    {
        nint handle = texture.Handle;

        for (int i = 0; i < texture_slot_count; i++)
        {
            if (slotHandles[i] != handle)
                continue;

            slotViews[i] = VkImageView.Null;
            slotSamplers[i] = VkSampler.Null;
            slotHandles[i] = -1;
            textureSetDirty = true;
        }
    }

    private void resetTextureSlots()
    {
        Array.Clear(slotViews);
        Array.Clear(slotSamplers);
        Array.Clear(slotHandles);
        slotCount = 0;
        textureSetDirty = true;
    }

    /// <summary>
    /// Starts the texture table with the white pixel at entry 0, so solid-color geometry costs no bind.
    /// </summary>
    private void resetTextureSlotsToWhite()
    {
        resetTextureSlots();

        if (whiteTexture == null)
            return;

        BindTexture(0, whiteTexture);
        slotCount = 1;
    }

    public void DrawVertices(ReadOnlySpan<SakuraVertex> vertices, Texture texture)
    {
        if (batch == null || vertices.Length == 0)
            return;

        float textureIndex = prepareTexture(texture);

        // The clip is stamped onto every vertex on the way into the batch; the fragment shader's
        // applyClipping reads it back.
        var clip = clips.Current;
        batch.AddRange(vertices, textureIndex, clip.ClipData, clip.ShearX, clip.Radius);
    }

    public void DrawQuads(ReadOnlySpan<SakuraVertex> vertices, Texture texture)
    {
        if (batch == null)
            return;

        float textureIndex = prepareTexture(texture);
        var clip = clips.Current;

        for (int i = 0; i + 4 <= vertices.Length; i += 4)
            batch.AddQuad(vertices.Slice(i, 4), textureIndex, clip.ClipData, clip.ShearX, clip.Radius);
    }

    /// <summary>
    /// Draws <paramref name="vertices"/> with whatever shader is in use, as they are: no clip is stamped
    /// onto them and the texture table is whatever the caller bound. A four-vertex span is a quad (TL,
    /// TR, BR, BL), anything else a triangle list.
    /// </summary>
    public void DrawVerticesRaw(ReadOnlySpan<SakuraVertex> vertices)
    {
        if (currentFrame == null || vertices.Length == 0)
            return;

        // This draws immediately, so anything still batched would end up behind it.
        FlushBatch();

        if (!ensurePassOpen())
            return;

        Span<SakuraVertex> quad = stackalloc SakuraVertex[6];
        scoped ReadOnlySpan<SakuraVertex> triangles = vertices;

        if (vertices.Length == 4)
        {
            quad[0] = vertices[0];
            quad[1] = vertices[1];
            quad[2] = vertices[2];
            quad[3] = vertices[2];
            quad[4] = vertices[3];
            quad[5] = vertices[0];
            triangles = quad;
        }

        var frame = currentFrame;
        ulong bytes = (ulong)(triangles.Length * SakuraVertex.Size);
        byte* destination = allocateStream(bytes, 16, out ulong offset);

        fixed (SakuraVertex* source = triangles)
            Buffer.MemoryCopy(source, destination, (long)bytes, (long)bytes);

        var vertexBuffer = frame.Stream.Buffer;
        var api = device!.Api;
        api.vkCmdBindVertexBuffers(frame.CommandBuffer, 0, 1, &vertexBuffer, &offset);

        bindState(frame);
        api.vkCmdDraw(frame.CommandBuffer, (uint)triangles.Length, 1, 0, 0);

        stat_draw_calls.Accumulator++;
        stat_vertices_drawn.Accumulator += triangles.Length;

        // The caller bound its own textures (an effect's source, the video planes), so the table no
        // longer describes what the batch assigned.
        resetTextureSlots();
    }

    /// <summary>
    /// Draws the pending batch with a single indexed draw. Every state change the recorded geometry
    /// depends on (blend mode, shader, a uniform block, a texture-table entry it uses) must call this
    /// first, or geometry recorded under one state is drawn under another.
    /// </summary>
    public void FlushBatch()
    {
        if (batch == null || batch.IsEmpty)
            return;

        // Nothing to draw into this frame (minimised, or between swapchains).
        if (!ensurePassOpen())
        {
            batch.Reset();
            return;
        }

        var frame = currentFrame!;
        var api = device!.Api;
        var commandBuffer = frame.CommandBuffer;

        int vertexCount = batch.VertexCount;
        int indexCount = batch.IndexCount;
        ulong vertexBytes = (ulong)(vertexCount * SakuraVertex.Size);

        byte* vertexData = allocateStream(vertexBytes, 16, out ulong vertexOffset);

        fixed (SakuraVertex* source = batch.Vertices)
            Buffer.MemoryCopy(source, vertexData, (long)vertexBytes, (long)vertexBytes);

        var vertexBuffer = frame.Stream.Buffer;

        if (batch.HasNonQuad)
        {
            ulong indexBytes = (ulong)indexCount * sizeof(uint);
            byte* indexData = allocateStream(indexBytes, 4, out ulong indexOffset);

            fixed (uint* source = batch.Indices)
                Buffer.MemoryCopy(source, indexData, (long)indexBytes, (long)indexBytes);

            // An index allocation can replace the stream buffer too; the vertices then live in the old one.
            api.vkCmdBindIndexBuffer(commandBuffer, frame.Stream.Buffer, indexOffset, VkIndexType.Uint32);
        }
        else
        {
            // The static buffer already holds the exact pattern for indices [0, indexCount).
            api.vkCmdBindIndexBuffer(commandBuffer, quadIndexBuffer, 0, VkIndexType.Uint32);
        }

        api.vkCmdBindVertexBuffers(commandBuffer, 0, 1, &vertexBuffer, &vertexOffset);

        bindState(frame);

        api.vkCmdDrawIndexed(commandBuffer, (uint)indexCount, 1, 0, 0, 0);

        stat_draw_calls.Accumulator++;
        stat_vertices_drawn.Accumulator += vertexCount;

        batch.Reset();
    }

    /// <summary>
    /// Brings the pipeline and both descriptor sets up to date for the next draw.
    /// </summary>
    private void bindState(VulkanFrameContext frame)
    {
        var api = device!.Api;
        var commandBuffer = frame.CommandBuffer;
        var shader = currentShader!;

        if (pipelineDirty)
        {
            var pipeline = pipelines!.Get(shader, currentBlendMode, currentPassFormat);

            if (pipeline != boundPipeline)
            {
                api.vkCmdBindPipeline(commandBuffer, VkPipelineBindPoint.Graphics, pipeline);
                boundPipeline = pipeline;
            }

            pipelineDirty = false;
        }

        if (uniformsDirty)
        {
            if (uniformSet.IsNull || uniformSetBuffer != frame.Stream.Buffer)
            {
                uniformSet = descriptors!.AllocateUniformSet(frame, frame.Stream.Buffer);
                uniformSetBuffer = frame.Stream.Buffer;
                stat_descriptor_sets.Accumulator++;
            }

            var set = uniformSet;

            fixed (uint* offsets = uniformOffsets)
                api.vkCmdBindDescriptorSets(commandBuffer, VkPipelineBindPoint.Graphics, shader.PipelineLayout, 0, 1, &set, VulkanShaderLayout.UNIFORM_BINDING_COUNT, offsets);

            uniformsDirty = false;
        }

        if (textureSetDirty)
        {
            var set = currentTextureSet(frame);
            api.vkCmdBindDescriptorSets(commandBuffer, VkPipelineBindPoint.Graphics, shader.PipelineLayout, 1, 1, &set, 0, null);
            textureSetDirty = false;
        }
    }

    /// <summary>
    /// A descriptor set holding the current texture table in the current shader's set-1 layout: one
    /// already written this frame when the table is one seen recently, or a new one. Entries nothing was
    /// bound to hold the white pixel.
    /// </summary>
    private VkDescriptorSet currentTextureSet(VulkanFrameContext frame)
    {
        var white = whiteTexture!;
        bool array = currentShader!.Layout.UsesTextureArray;
        int count = array ? texture_slot_count : (int)VulkanShaderLayout.SINGLE_TEXTURE_COUNT;

        // The unused tail stays white too, so a cached table is compared over its full length.
        for (int i = 0; i < texture_slot_count; i++)
        {
            bool assigned = i < count && slotViews[i].IsNotNull;
            setViews[i] = assigned ? slotViews[i] : white.View;
            setSamplers[i] = assigned ? slotSamplers[i] : white.Sampler;
        }

        var viewKey = MemoryMarshal.Cast<VkImageView, ulong>(setViews);
        var samplerKey = MemoryMarshal.Cast<VkSampler, ulong>(setSamplers);

        for (int i = 0; i < cachedSetCount; i++)
        {
            if (cachedSetIsArray[i] != array)
                continue;

            var cachedViews = MemoryMarshal.Cast<VkImageView, ulong>(cachedSetViews.AsSpan(i * texture_slot_count, texture_slot_count));
            var cachedSamplers = MemoryMarshal.Cast<VkSampler, ulong>(cachedSetSamplers.AsSpan(i * texture_slot_count, texture_slot_count));

            if (viewKey.SequenceEqual(cachedViews) && samplerKey.SequenceEqual(cachedSamplers))
                return cachedSets[i];
        }

        var set = array
            ? descriptors!.AllocateTextureArraySet(frame, setViews, setSamplers)
            : descriptors!.AllocateSingleTextureSet(frame, setViews.AsSpan(0, count), setSamplers.AsSpan(0, count));

        stat_descriptor_sets.Accumulator++;

        int entry = cachedSetNext;
        cachedSetNext = (cachedSetNext + 1) % texture_set_cache_size;
        cachedSetCount = Math.Max(cachedSetCount, entry + 1);

        setViews.CopyTo(cachedSetViews.AsSpan(entry * texture_slot_count));
        setSamplers.CopyTo(cachedSetSamplers.AsSpan(entry * texture_slot_count));
        cachedSets[entry] = set;
        cachedSetIsArray[entry] = array;

        return set;
    }

    public void SetBlendMode(BlendingMode blendingMode)
    {
        if (blendingMode == currentBlendMode)
            return;

        // The blend state is part of the pipeline, so the pending batch must go out under the old one.
        stat_state_change_flushes.Accumulator++;
        FlushBatch();

        currentBlendMode = blendingMode;
        pipelineDirty = true;
    }

    /// <summary>
    /// Makes <paramref name="shader"/> the shader subsequent draws use. Called by
    /// <see cref="VulkanShader.Use"/>.
    /// </summary>
    internal void UseShader(VulkanShader shader)
    {
        if (shader == currentShader)
            return;

        FlushBatch();
        currentShader = shader;
        pipelineDirty = true;
        uniformsDirty = true;
        textureSetDirty = true;
    }

    public void RestoreMainShader()
    {
        FlushBatch();

        currentShader = mainShader;
        pipelineDirty = true;
        uniformsDirty = true;

        maskState.IsBorder = 0;
        maskState.IsEdgeEffect = 0;
        writeUniform(1, in maskState);

        var block = new ProjectionBlock { Projection = projectionMatrix };
        writeUniform(0, in block);

        resetTextureSlotsToWhite();
    }

    public void ApplyCurrentClip(Span<SakuraVertex> vertices) => clips.ApplyTo(vertices);

    public void PushMask(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius)
        => clips.Push(maskCenter, maskHalfSize, shearX, cornerRadius);

    public void PopMask(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius, float borderThickness, Color borderColor, ReadOnlySpan<SakuraVertex> maskVertices = default)
    {
        clips.Pop();
        drawBorder(maskCenter, maskHalfSize, shearX, cornerRadius, borderThickness, borderColor, maskVertices);
    }

    /// <summary>
    /// Draws the rounded/sheared border ring via the main shader's border path (u_IsBorder). Border
    /// geometry and color travel through MaskBlock.
    /// </summary>
    private void drawBorder(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius, float borderThickness, Color borderColor, ReadOnlySpan<SakuraVertex> vertices)
    {
        if (borderThickness <= 0 || vertices.Length < 4)
            return;

        // The new MaskBlock applies to every draw from here on, so pending geometry has to be drawn under
        // the previous one first.
        FlushBatch();

        maskState.IsBorder = 1;
        maskState.MaskCenter = new Vector2(maskCenter.X, maskCenter.Y);
        maskState.MaskHalfSize = new Vector2(maskHalfSize.X, maskHalfSize.Y);
        maskState.ShearX = shearX;
        maskState.CornerRadius = cornerRadius;
        maskState.BorderThickness = borderThickness;
        maskState.BorderColor = new Vector4(borderColor.R / 255f, borderColor.G / 255f, borderColor.B / 255f, borderColor.A / 255f);
        writeUniform(1, in maskState);

        DrawQuads(vertices[..4], WhitePixel);
        FlushBatch();

        maskState.IsBorder = 0;
        writeUniform(1, in maskState);
    }

    /// <summary>
    /// Draws a soft edge effect (glow/shadow) via the main shader's edge-effect path (u_IsEdgeEffect).
    /// </summary>
    public void DrawEdgeEffect(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius, float edgeRadius, Vector2 offset, Color color, bool glow, bool hollow, ReadOnlySpan<SakuraVertex> quadVertices)
    {
        if (color.A == 0 || quadVertices.Length < 4)
            return;

        // As in drawBorder: the MaskBlock (and the glow blend swap) would otherwise apply retroactively to
        // pending geometry.
        FlushBatch();

        var previousBlend = currentBlendMode;
        if (glow)
            SetBlendMode(BlendingMode.Additive);

        maskState.IsEdgeEffect = 1;
        maskState.MaskCenter = new Vector2(maskCenter.X, maskCenter.Y);
        maskState.MaskHalfSize = new Vector2(maskHalfSize.X, maskHalfSize.Y);
        maskState.ShearX = shearX;
        maskState.CornerRadius = cornerRadius;
        maskState.EdgeRadius = edgeRadius;
        maskState.EdgeOffset = new Vector2(offset.X, offset.Y);
        maskState.EdgeHollow = hollow ? 1 : 0;
        maskState.EdgeGlow = glow ? 1 : 0;
        maskState.BorderColor = new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
        writeUniform(1, in maskState);

        DrawQuads(quadVertices[..4], WhitePixel);
        FlushBatch();

        maskState.IsEdgeEffect = 0;
        writeUniform(1, in maskState);

        if (glow)
            SetBlendMode(previousBlend);
    }

    /// <summary>
    /// Redirects drawing into <paramref name="frameBuffer"/>, cleared to <paramref name="clearColor"/>.
    /// The projection maps <paramref name="sourceRect"/> onto it, so draw nodes keep their screen-space
    /// coordinates.
    /// </summary>
    public void BindFrameBuffer(IFrameBuffer frameBuffer, RectangleF sourceRect, Color clearColor = default)
    {
        if (currentFrame == null || frameBuffer is not VulkanFrameBuffer target)
            return;

        // Anything batched so far belongs to the previous target.
        FlushBatch();

        targetStack.Push(new TargetState(currentTarget, projectionMatrix, clips.Current));
        endPass();

        // The same matrix Metal uses offscreen. Vulkan's clip space points +Y down, so this puts the
        // rect's top edge on the image's last row: the orientation GL leaves a framebuffer in, which the
        // V-flipped texture coordinates BufferedContainer composites with expect.
        projectionMatrix = Matrix4x4.CreateOrthographicOffCenter(
            sourceRect.X, sourceRect.X + sourceRect.Width,
            sourceRect.Y + sourceRect.Height, sourceRect.Y,
            -1, 1);

        var projection = new ProjectionBlock { Projection = projectionMatrix };
        writeUniform(0, in projection);

        // Content inside the buffer starts from a clean clip; the outer clip applies to the composited
        // quad instead.
        clips.Current = ClipState.None;

        currentTarget = target;
        targetNeedsReopen = false;

        var clear = new VkClearColorValue(clearColor.R / 255f, clearColor.G / 255f, clearColor.B / 255f, clearColor.A / 255f);
        beginPass(renderPasses!.Get(target.Format, VulkanPassKind.OffscreenClear), target.Handle, target.Format, (uint)target.Width, (uint)target.Height, clear);

        // Sampled by whatever is drawn after this pass, once it has run.
        target.ColorTexture.MarkRendered();

        // The table may still hold this image from an earlier composite, and an image can't be sampled
        // while it is the attachment being drawn into.
        resetTextureSlotsToWhite();
    }

    /// <summary>
    /// Ends drawing into the most recently bound framebuffer and returns to the previous target, with its
    /// projection and clip. The previous target's pass is reopened when something is next drawn into it.
    /// </summary>
    public void UnbindFrameBuffer()
    {
        if (targetStack.Count == 0)
            throw new InvalidOperationException($"{nameof(UnbindFrameBuffer)} was called without a matching {nameof(BindFrameBuffer)}.");

        FlushBatch();
        endPass();

        var state = targetStack.Pop();
        currentTarget = state.FrameBuffer;
        targetNeedsReopen = true;

        projectionMatrix = state.Projection;
        var projection = new ProjectionBlock { Projection = projectionMatrix };
        writeUniform(0, in projection);

        clips.Current = state.Clip;

        resetTextureSlotsToWhite();
    }

    #endregion

    #region Uniforms and stream memory

    /// <summary>
    /// Sets the contents of the uniform block at <paramref name="binding"/> for draws from here on. The
    /// caller flushes first if pending geometry must keep the old contents.
    /// </summary>
    internal void SetUniformBlock<T>(uint binding, in T data) where T : unmanaged => writeUniform(binding, in data);

    private void writeUniform<T>(uint binding, in T data) where T : unmanaged
    {
        if (binding >= VulkanShaderLayout.UNIFORM_BINDING_COUNT || sizeof(T) > VulkanShaderLayout.UNIFORM_BLOCK_RANGE)
            throw new ArgumentOutOfRangeException(nameof(binding), $"A {sizeof(T)}-byte block at binding {binding} doesn't fit the uniform layout.");

        var shadow = uniformShadow.AsSpan((int)(binding * VulkanShaderLayout.UNIFORM_BLOCK_RANGE), (int)VulkanShaderLayout.UNIFORM_BLOCK_RANGE);
        shadow.Clear();

        fixed (T* source = &data)
            new ReadOnlySpan<byte>(source, sizeof(T)).CopyTo(shadow);

        uniformWritten[binding] = true;

        if (currentFrame != null)
            uploadUniform(binding);
    }

    /// <summary>
    /// Copies the block at <paramref name="binding"/> to a fresh offset of the stream buffer.
    /// </summary>
    private void uploadUniform(uint binding)
    {
        byte* destination = allocateStream(VulkanShaderLayout.UNIFORM_BLOCK_RANGE, uniformAlignment, out ulong offset);

        var shadow = uniformShadow.AsSpan((int)(binding * VulkanShaderLayout.UNIFORM_BLOCK_RANGE), (int)VulkanShaderLayout.UNIFORM_BLOCK_RANGE);
        shadow.CopyTo(new Span<byte>(destination, (int)VulkanShaderLayout.UNIFORM_BLOCK_RANGE));

        uniformOffsets[binding] = (uint)offset;
        uniformsDirty = true;
    }

    /// <summary>
    /// Writes every block that has contents into the current stream buffer, whose offsets are the only
    /// ones the set 0 bound from it can reach.
    /// </summary>
    private void rewriteUniforms()
    {
        for (uint i = 0; i < VulkanShaderLayout.UNIFORM_BINDING_COUNT; i++)
        {
            if (uniformWritten[i])
                uploadUniform(i);
            else
                uniformOffsets[i] = 0;
        }
    }

    /// <summary>
    /// Reserves bytes in the current frame's stream buffer. When that replaces the buffer, every uniform
    /// block is written again into the new one, since the set 0 rebuilt for it can't see the old.
    /// </summary>
    private byte* allocateStream(ulong size, ulong alignment, out ulong offset)
    {
        if (!currentFrame!.AllocateStream(size, alignment, out offset, out byte* pointer))
            return pointer;

        uniformSet = VkDescriptorSet.Null;
        rewriteUniforms();
        return pointer;
    }

    #endregion

    #region Uploads

    /// <summary>
    /// Records a copy of <paramref name="data"/>, tightly packed rows of RGBA, into a region of
    /// <paramref name="texture"/>.
    /// </summary>
    internal void UploadTexture(VulkanTexture texture, int x, int y, int width, int height, ReadOnlySpan<byte> data)
    {
        fixed (byte* source = data)
            upload(texture, x, y, width, height, source, (long)width * texture.TexelBytes);
    }

    /// <summary>
    /// Records a copy of a decoded video plane, its rows <paramref name="stride"/> bytes apart, into the
    /// top-left of <paramref name="plane"/>. Called by <see cref="VulkanTexture.UploadPlane"/>.
    /// </summary>
    internal void UploadPlane(VulkanTexture plane, byte* data, int stride, int width, int height)
        => upload(plane, 0, 0, width, height, data, stride);

    /// <summary>
    /// Stages <paramref name="height"/> rows read <paramref name="stride"/> bytes apart from
    /// <paramref name="source"/> and records their copy into a region of <paramref name="texture"/>.
    /// During a frame the copy goes into the frame's upload command buffer; outside one (at startup) it is
    /// submitted and waited for on the spot.
    /// </summary>
    private void upload(VulkanTexture texture, int x, int y, int width, int height, byte* source, long stride)
    {
        long rowBytes = (long)width * texture.TexelBytes;

        // The rows keep the source's stride when it is a whole number of texels, since the copy can step
        // over padding itself: a decoded frame, whose rows are padded for alignment, is then one memcpy.
        // A stride that splits a texel, or runs backwards, is packed row by row instead.
        long stagedStride = stride >= rowBytes && stride % texture.TexelBytes == 0 ? stride : rowBytes;
        ulong bytes = (ulong)(stagedStride * (height - 1) + rowBytes);
        uint rowLength = (uint)(stagedStride / texture.TexelBytes);

        stat_staging_bytes.Accumulator += (long)bytes;

        if (currentFrame == null)
        {
            var staging = stagingPool!.Rent(bytes);

            try
            {
                copyRows(source, stride, staging.Mapped, stagedStride, rowBytes, height);
                flushStaging(staging.Allocation);

                var immediate = beginImmediate();
                recordCopy(immediate, texture, staging.Buffer, 0, rowLength, x, y, width, height);
                finishTexture(immediate, texture);
                submitImmediate(immediate);
            }
            finally
            {
                // The queue has drained, so nothing reads it any more.
                stagingPool.Return(staging);
            }

            return;
        }

        var frame = currentFrame;
        var commandBuffer = frame.BeginUploads();

        VkBuffer stagingBuffer;
        ulong stagingOffset;

        if (bytes > dedicated_staging_threshold)
        {
            var staging = frame.RentStaging(bytes);
            copyRows(source, stride, staging.Mapped, stagedStride, rowBytes, height);
            flushStaging(staging.Allocation);
            stagingBuffer = staging.Buffer;
            stagingOffset = 0;
        }
        else
        {
            byte* destination = allocateStream(bytes, stagingAlignment, out stagingOffset);
            copyRows(source, stride, destination, stagedStride, rowBytes, height);
            stagingBuffer = frame.Stream.Buffer;
        }

        recordCopy(commandBuffer, texture, stagingBuffer, stagingOffset, rowLength, x, y, width, height);

        if (!texture.AwaitingFinish)
        {
            texture.AwaitingFinish = true;
            texturesAwaitingFinish.Add(texture);
        }
    }

    private static void copyRows(byte* source, long sourceStride, byte* destination, long destinationStride, long rowBytes, int rows)
    {
        if (sourceStride == destinationStride)
        {
            long total = sourceStride * (rows - 1) + rowBytes;
            Buffer.MemoryCopy(source, destination, total, total);
            return;
        }

        for (int row = 0; row < rows; row++)
            Buffer.MemoryCopy(source + row * sourceStride, destination + row * destinationStride, rowBytes, rowBytes);
    }

    /// <summary>
    /// Records the copy of a staged region into <paramref name="texture"/>, moving it into the transfer
    /// layout first. Staged rows are <paramref name="rowLength"/> texels apart.
    /// </summary>
    private void recordCopy(VkCommandBuffer commandBuffer, VulkanTexture texture, VkBuffer buffer, ulong offset, uint rowLength, int x, int y, int width, int height)
    {
        var api = device!.Api;

        if (texture.Layout != VkImageLayout.TransferDstOptimal)
        {
            // From being sampled, the copy only has to wait for the fragment shaders that read the old
            // contents (frames still in flight may be among them); from UNDEFINED, for nothing.
            bool sampledBefore = texture.Layout != VkImageLayout.Undefined;

            transition(commandBuffer, texture, 0, texture.MipLevels, texture.Layout, VkImageLayout.TransferDstOptimal,
                sampledBefore ? VkPipelineStageFlags.FragmentShader : VkPipelineStageFlags.TopOfPipe, VkAccessFlags.None,
                VkPipelineStageFlags.Transfer, VkAccessFlags.TransferWrite);

            texture.Layout = VkImageLayout.TransferDstOptimal;
        }
        else
        {
            // A second copy into the same image this frame: order it after the first, which may cover
            // the same texels.
            transition(commandBuffer, texture, 0, 1, VkImageLayout.TransferDstOptimal, VkImageLayout.TransferDstOptimal,
                VkPipelineStageFlags.Transfer, VkAccessFlags.TransferWrite,
                VkPipelineStageFlags.Transfer, VkAccessFlags.TransferWrite);
        }

        var region = new VkBufferImageCopy
        {
            bufferOffset = offset,
            bufferRowLength = rowLength,
            bufferImageHeight = 0,
            imageSubresource = new VkImageSubresourceLayers(VkImageAspectFlags.Color, 0, 0, 1),
            imageOffset = new VkOffset3D(x, y, 0),
            imageExtent = new VkExtent3D { width = (uint)width, height = (uint)height, depth = 1 },
        };

        api.vkCmdCopyBufferToImage(commandBuffer, buffer, texture.Image, VkImageLayout.TransferDstOptimal, 1, &region);
    }

    /// <summary>
    /// Regenerates the mip chain of every texture copied into this frame and hands them back to the
    /// fragment shader. Recorded at the end of the upload command buffer, once, however many copies
    /// each texture received.
    /// </summary>
    private void finishUploads(VkCommandBuffer commandBuffer)
    {
        foreach (var texture in texturesAwaitingFinish)
        {
            texture.AwaitingFinish = false;

            // Released mid-frame: it is destroyed once this frame finishes, and nothing samples it before.
            if (!texture.IsReleased)
                finishTexture(commandBuffer, texture);
        }

        texturesAwaitingFinish.Clear();
    }

    private void finishTexture(VkCommandBuffer commandBuffer, VulkanTexture texture)
    {
        var api = device!.Api;
        uint levels = texture.MipLevels;

        if (levels > 1)
        {
            int width = texture.Width;
            int height = texture.Height;

            for (uint level = 1; level < levels; level++)
            {
                // The level above was written by the copy or the previous blit, and is read from now on.
                transition(commandBuffer, texture, level - 1, 1, VkImageLayout.TransferDstOptimal, VkImageLayout.TransferSrcOptimal,
                    VkPipelineStageFlags.Transfer, VkAccessFlags.TransferWrite,
                    VkPipelineStageFlags.Transfer, VkAccessFlags.TransferRead);

                int nextWidth = Math.Max(1, width / 2);
                int nextHeight = Math.Max(1, height / 2);

                var blit = new VkImageBlit
                {
                    srcSubresource = new VkImageSubresourceLayers(VkImageAspectFlags.Color, level - 1, 0, 1),
                    dstSubresource = new VkImageSubresourceLayers(VkImageAspectFlags.Color, level, 0, 1),
                };

                blit.srcOffsets[1] = new VkOffset3D(width, height, 1);
                blit.dstOffsets[1] = new VkOffset3D(nextWidth, nextHeight, 1);

                api.vkCmdBlitImage(commandBuffer, texture.Image, VkImageLayout.TransferSrcOptimal, texture.Image, VkImageLayout.TransferDstOptimal, 1, &blit, VkFilter.Linear);

                width = nextWidth;
                height = nextHeight;
            }

            // Every level but the last was read as a blit source; the last was only written.
            transition(commandBuffer, texture, 0, levels - 1, VkImageLayout.TransferSrcOptimal, VkImageLayout.ShaderReadOnlyOptimal,
                VkPipelineStageFlags.Transfer, VkAccessFlags.TransferRead,
                VkPipelineStageFlags.FragmentShader, VkAccessFlags.ShaderRead);
        }

        transition(commandBuffer, texture, levels - 1, 1, VkImageLayout.TransferDstOptimal, VkImageLayout.ShaderReadOnlyOptimal,
            VkPipelineStageFlags.Transfer, VkAccessFlags.TransferWrite,
            VkPipelineStageFlags.FragmentShader, VkAccessFlags.ShaderRead);

        texture.Layout = VkImageLayout.ShaderReadOnlyOptimal;
    }

    private void transition(VkCommandBuffer commandBuffer, VulkanTexture texture, uint baseLevel, uint levelCount, VkImageLayout oldLayout, VkImageLayout newLayout,
                            VkPipelineStageFlags srcStage, VkAccessFlags srcAccess, VkPipelineStageFlags dstStage, VkAccessFlags dstAccess)
    {
        var barrier = new VkImageMemoryBarrier
        {
            srcAccessMask = srcAccess,
            dstAccessMask = dstAccess,
            oldLayout = oldLayout,
            newLayout = newLayout,
            srcQueueFamilyIndex = Vortice.Vulkan.Vulkan.VK_QUEUE_FAMILY_IGNORED,
            dstQueueFamilyIndex = Vortice.Vulkan.Vulkan.VK_QUEUE_FAMILY_IGNORED,
            image = texture.Image,
            subresourceRange = new VkImageSubresourceRange
            {
                aspectMask = VkImageAspectFlags.Color,
                baseMipLevel = baseLevel,
                levelCount = levelCount,
                layerCount = 1,
            },
        };

        device!.Api.vkCmdPipelineBarrier(commandBuffer, srcStage, dstStage, VkDependencyFlags.None, 0, null, 0, null, 1, &barrier);
    }

    /// <summary>
    /// Makes host writes into <paramref name="allocation"/> visible to the device, where the memory isn't
    /// coherent. Must come before the submission that reads them.
    /// </summary>
    private void flushStaging(in VulkanAllocation allocation)
    {
        if (allocation.Coherent)
            return;

        var range = new VkMappedMemoryRange { memory = allocation.Memory, offset = 0, size = Vortice.Vulkan.Vulkan.VK_WHOLE_SIZE };
        VulkanDevice.Check(device!.Api.vkFlushMappedMemoryRanges(1, &range), "vkFlushMappedMemoryRanges");
    }

    private (VkBuffer Buffer, VulkanAllocation Allocation) createStagingBuffer(ulong size, ReadOnlySpan<byte> contents)
    {
        var createInfo = new VkBufferCreateInfo
        {
            size = size,
            usage = VkBufferUsageFlags.TransferSrc,
            sharingMode = VkSharingMode.Exclusive,
        };

        VkBuffer buffer;
        VulkanDevice.Check(device!.Api.vkCreateBuffer(&createInfo, null, &buffer), "vkCreateBuffer");

        var allocation = memory!.BindHostVisibleBuffer(buffer);
        contents.CopyTo(new Span<byte>(allocation.Mapped, contents.Length));
        flushStaging(allocation);

        return (buffer, allocation);
    }

    private void destroyStagingBuffer(VkBuffer buffer, VulkanAllocation allocation)
    {
        device!.Api.vkDestroyBuffer(buffer, null);
        memory!.Free(allocation);
    }

    private void createImmediateCommandPool()
    {
        var poolInfo = new VkCommandPoolCreateInfo
        {
            flags = VkCommandPoolCreateFlags.Transient,
            queueFamilyIndex = device!.GraphicsQueueFamily,
        };

        VkCommandPool pool;
        VulkanDevice.Check(device.Api.vkCreateCommandPool(&poolInfo, null, &pool), "vkCreateCommandPool");
        immediateCommandPool = pool;
    }

    private VkCommandBuffer beginImmediate()
    {
        var api = device!.Api;

        var allocateInfo = new VkCommandBufferAllocateInfo
        {
            commandPool = immediateCommandPool,
            level = VkCommandBufferLevel.Primary,
            commandBufferCount = 1,
        };

        VkCommandBuffer commandBuffer;
        VulkanDevice.Check(api.vkAllocateCommandBuffers(&allocateInfo, &commandBuffer), "vkAllocateCommandBuffers");

        var beginInfo = new VkCommandBufferBeginInfo { flags = VkCommandBufferUsageFlags.OneTimeSubmit };
        VulkanDevice.Check(api.vkBeginCommandBuffer(commandBuffer, &beginInfo), "vkBeginCommandBuffer");
        return commandBuffer;
    }

    /// <summary>
    /// Submits <paramref name="commandBuffer"/> and waits for the queue to drain. Only for work outside a
    /// frame, which is rare enough (startup) that the stall doesn't matter.
    /// </summary>
    private void submitImmediate(VkCommandBuffer commandBuffer)
    {
        var api = device!.Api;
        VulkanDevice.Check(api.vkEndCommandBuffer(commandBuffer), "vkEndCommandBuffer");

        var submitInfo = new VkSubmitInfo
        {
            commandBufferCount = 1,
            pCommandBuffers = &commandBuffer,
        };

        VulkanDevice.Check(api.vkQueueSubmit(device.GraphicsQueue, 1, &submitInfo, VkFence.Null), "vkQueueSubmit");
        VulkanDevice.Check(api.vkQueueWaitIdle(device.GraphicsQueue), "vkQueueWaitIdle");

        api.vkFreeCommandBuffers(immediateCommandPool, 1, &commandBuffer);
    }

    /// <summary>
    /// The index pattern of every quad the batch can hold (0, 1, 2, 2, 3, 0, then the same from 4 on),
    /// uploaded once into device-local memory, so a batch of nothing but quads uploads vertices only.
    /// </summary>
    private void createQuadIndexBuffer()
    {
        const int max_quads = max_batch_vertices / 4;
        ulong size = max_quads * 6 * sizeof(uint);

        uint[] indices = new uint[max_quads * 6];

        for (int q = 0; q < max_quads; q++)
        {
            uint first = (uint)(q * 4);
            int o = q * 6;
            indices[o + 0] = first;
            indices[o + 1] = first + 1;
            indices[o + 2] = first + 2;
            indices[o + 3] = first + 2;
            indices[o + 4] = first + 3;
            indices[o + 5] = first;
        }

        var createInfo = new VkBufferCreateInfo
        {
            size = size,
            usage = VkBufferUsageFlags.IndexBuffer | VkBufferUsageFlags.TransferDst,
            sharingMode = VkSharingMode.Exclusive,
        };

        var api = device!.Api;
        VkBuffer buffer;
        VulkanDevice.Check(api.vkCreateBuffer(&createInfo, null, &buffer), "vkCreateBuffer");
        quadIndexBuffer = buffer;
        quadIndexAllocation = memory!.BindDeviceLocalBuffer(buffer);
        device.SetName(VkObjectType.Buffer, buffer.Handle, "Quad indices");

        var (staging, stagingAllocation) = createStagingBuffer(size, MemoryMarshal.AsBytes(indices.AsSpan()));

        try
        {
            var commandBuffer = beginImmediate();

            var copy = new VkBufferCopy { srcOffset = 0, dstOffset = 0, size = size };
            api.vkCmdCopyBuffer(commandBuffer, staging, buffer, 1, &copy);

            var barrier = new VkBufferMemoryBarrier
            {
                srcAccessMask = VkAccessFlags.TransferWrite,
                dstAccessMask = VkAccessFlags.IndexRead,
                srcQueueFamilyIndex = Vortice.Vulkan.Vulkan.VK_QUEUE_FAMILY_IGNORED,
                dstQueueFamilyIndex = Vortice.Vulkan.Vulkan.VK_QUEUE_FAMILY_IGNORED,
                buffer = buffer,
                offset = 0,
                size = Vortice.Vulkan.Vulkan.VK_WHOLE_SIZE,
            };

            api.vkCmdPipelineBarrier(commandBuffer, VkPipelineStageFlags.Transfer, VkPipelineStageFlags.VertexInput, VkDependencyFlags.None, 0, null, 1, &barrier, 0, null);

            submitImmediate(commandBuffer);
        }
        finally
        {
            destroyStagingBuffer(staging, stagingAllocation);
        }
    }

    private void createWhitePixel()
    {
        whiteTexture = new VulkanTexture(this, device!, memory!, 1, 1, descriptors!.TextureSampler, false);
        whiteTexture.Upload(new byte[] { 255, 255, 255, 255 });
        WhitePixel = new Texture(whiteTexture, TextureOwnership.Shared);
    }

    /// <summary>
    /// Whether mip levels can be generated on the GPU: the texture format must allow linear-filtered
    /// blits from and into it. Required by the spec for this format, so this only fails on a broken
    /// driver, which then gets single-level textures rather than an error.
    /// </summary>
    private static bool supportsMipmapGeneration(VulkanDevice device)
    {
        VkFormatProperties properties;
        device.InstanceApi.vkGetPhysicalDeviceFormatProperties(device.PhysicalDevice, VulkanTexture.FORMAT, &properties);

        const VkFormatFeatureFlags needed = VkFormatFeatureFlags.BlitSrc | VkFormatFeatureFlags.BlitDst | VkFormatFeatureFlags.SampledImageFilterLinear;
        return (properties.optimalTilingFeatures & needed) == needed;
    }

    #endregion

    #region Readback

    /// <summary>
    /// sRGB-encoded 8-bit values decoded to linear, the space the shaders blend in and
    /// <see cref="PixelSurface"/> holds.
    /// </summary>
    private static readonly float[] srgb_to_linear = buildSrgbTable();

    private static float[] buildSrgbTable()
    {
        float[] table = new float[256];

        for (int i = 0; i < table.Length; i++)
        {
            float c = i / 255f;
            table[i] = c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
        }

        return table;
    }

    /// <summary>
    /// Copies the offscreen target back as it stands after the last submitted frame, in linear color
    /// with row 0 at the top. Waits for the GPU, so it is for tests, not frames.
    /// </summary>
    internal PixelSurface ReadOffscreen()
    {
        var screen = offscreenScreen ?? throw new InvalidOperationException($"{nameof(ReadOffscreen)} needs {nameof(InitializeOffscreen)}.");

        if (currentFrame != null)
            throw new InvalidOperationException("A frame is still being recorded; read back after Draw.");

        var texture = screen.ColorTexture;
        ulong size = (ulong)texture.Width * (ulong)texture.Height * 4;

        var createInfo = new VkBufferCreateInfo
        {
            size = size,
            usage = VkBufferUsageFlags.TransferDst,
            sharingMode = VkSharingMode.Exclusive,
        };

        var api = device!.Api;
        VkBuffer buffer;
        VulkanDevice.Check(api.vkCreateBuffer(&createInfo, null, &buffer), "vkCreateBuffer");
        var allocation = memory!.BindHostVisibleBuffer(buffer);

        try
        {
            var commandBuffer = beginImmediate();

            // The last frame's pass left the image ready to sample, written as a color attachment.
            transition(commandBuffer, texture, 0, 1, VkImageLayout.ShaderReadOnlyOptimal, VkImageLayout.TransferSrcOptimal,
                VkPipelineStageFlags.ColorAttachmentOutput, VkAccessFlags.ColorAttachmentWrite,
                VkPipelineStageFlags.Transfer, VkAccessFlags.TransferRead);

            var region = new VkBufferImageCopy
            {
                imageSubresource = new VkImageSubresourceLayers(VkImageAspectFlags.Color, 0, 0, 1),
                imageExtent = new VkExtent3D { width = (uint)texture.Width, height = (uint)texture.Height, depth = 1 },
            };

            api.vkCmdCopyImageToBuffer(commandBuffer, texture.Image, VkImageLayout.TransferSrcOptimal, buffer, 1, &region);

            transition(commandBuffer, texture, 0, 1, VkImageLayout.TransferSrcOptimal, VkImageLayout.ShaderReadOnlyOptimal,
                VkPipelineStageFlags.Transfer, VkAccessFlags.None,
                VkPipelineStageFlags.FragmentShader, VkAccessFlags.ShaderRead);

            var hostBarrier = new VkBufferMemoryBarrier
            {
                srcAccessMask = VkAccessFlags.TransferWrite,
                dstAccessMask = VkAccessFlags.HostRead,
                srcQueueFamilyIndex = Vortice.Vulkan.Vulkan.VK_QUEUE_FAMILY_IGNORED,
                dstQueueFamilyIndex = Vortice.Vulkan.Vulkan.VK_QUEUE_FAMILY_IGNORED,
                buffer = buffer,
                offset = 0,
                size = Vortice.Vulkan.Vulkan.VK_WHOLE_SIZE,
            };

            api.vkCmdPipelineBarrier(commandBuffer, VkPipelineStageFlags.Transfer, VkPipelineStageFlags.Host, VkDependencyFlags.None, 0, null, 1, &hostBarrier, 0, null);

            submitImmediate(commandBuffer);

            if (!allocation.Coherent)
            {
                var range = new VkMappedMemoryRange { memory = allocation.Memory, offset = 0, size = Vortice.Vulkan.Vulkan.VK_WHOLE_SIZE };
                VulkanDevice.Check(api.vkInvalidateMappedMemoryRanges(1, &range), "vkInvalidateMappedMemoryRanges");
            }

            bool bgra = texture.Format is VkFormat.B8G8R8A8Srgb or VkFormat.B8G8R8A8Unorm;
            var surface = new PixelSurface(texture.Width, texture.Height);
            byte* pixels = allocation.Mapped;

            for (int y = 0; y < texture.Height; y++)
            {
                for (int x = 0; x < texture.Width; x++)
                {
                    byte* p = pixels + ((long)y * texture.Width + x) * 4;
                    float r = srgb_to_linear[bgra ? p[2] : p[0]];
                    float g = srgb_to_linear[p[1]];
                    float b = srgb_to_linear[bgra ? p[0] : p[2]];

                    // Alpha is stored linearly in sRGB formats.
                    surface[x, y] = new Vector4(r, g, b, p[3] / 255f);
                }
            }

            return surface;
        }
        finally
        {
            api.vkDestroyBuffer(buffer, null);
            memory.Free(allocation);
        }
    }

    #endregion

    #region Resources

    public void ScheduleToDrawThread(Action action) => drawThreadQueue.Enqueue(action);

    public void ScheduleTextureUpload(Action upload, long approximateBytes) => textureUploadQueue.Enqueue(upload, approximateBytes);

    public INativeTexture CreateNativeTexture(int width, int height)
        => new VulkanTexture(this, device!, memory!, width, height, descriptors!.TextureSampler, mipmapsSupported && (width > 1 || height > 1));

    /// <summary>
    /// Compiles a shader pair to SPIR-V (cached in <see cref="ShaderCache"/>) and checks it against the
    /// renderer's resource layout. Pipelines for it are compiled the first time it draws with each blend
    /// mode.
    /// </summary>
    /// <exception cref="InvalidOperationException">The shader doesn't fit the resource layout; the
    /// message lists every problem.</exception>
    public IShader CreateShader(Storage storage, string vertexPath, string fragmentPath)
        => new VulkanShader(this, device!, ShaderCompiler.GetOrCompileSpirv(storage, vertexPath, fragmentPath, ShaderCache, DebugUtils.IsDebugBuild), $"{vertexPath}+{fragmentPath}", descriptors!);

    /// <summary>
    /// Releases every pipeline built from <paramref name="shader"/> along with its modules, once the GPU
    /// can no longer be using them. Called by <see cref="VulkanShader.Dispose"/>.
    /// </summary>
    internal void ReleaseShader(VulkanShader shader, Action destroyModules)
    {
        if (currentShader == shader)
        {
            FlushBatch();
            currentShader = mainShader;
            pipelineDirty = true;
        }

        pipelines?.Release(shader, pipeline => Retire(() => device!.Api.vkDestroyPipeline(pipeline, null)));
        Retire(destroyModules);
    }

    /// <remarks>
    /// Always a copying texture: <see cref="CanSampleHardwareFrame"/> never says yes, so
    /// <paramref name="fromHardwareFrame"/> is never true here.
    /// </remarks>
    public INativeVideoTexture CreateVideoTexture(int width, int height, VideoPlaneLayout layout, bool fromHardwareFrame = false)
        => new VulkanVideoTexture(this, device!, memory!, descriptors!.RenderTargetSampler, width, height, layout);

    /// <summary>
    /// Always false: hardware frames are read back by the decoder and copied in.
    /// </summary>
    public bool CanSampleHardwareFrame(FFmpeg.AutoGen.AVFrame* frame) => false;

    public IFrameBuffer CreateFrameBuffer(int width, int height, bool pixelSnapping = false)
        => new VulkanFrameBuffer(this, device!, memory!, renderPasses!, chooseOffscreenFormat(), pixelSnapping ? descriptors!.PixelSnappingSampler : descriptors!.RenderTargetSampler, width, height);

    /// <summary>
    /// The format of every offscreen framebuffer: the swapchain's when it is an sRGB format that can be
    /// rendered to, blended into and sampled, so offscreen and onscreen passes share one set of pipelines;
    /// otherwise RGBA8 sRGB, which the spec requires to support all of that.
    /// </summary>
    private VkFormat chooseOffscreenFormat()
    {
        if (offscreenFormat is VkFormat chosen)
            return chosen;

        var format = VkFormat.R8G8B8A8Srgb;
        var swapchainFormat = swapchain?.Format ?? VkFormat.Undefined;

        if (swapchain != null && swapchain.Handle.IsNotNull && swapchainFormat is VkFormat.B8G8R8A8Srgb or VkFormat.R8G8B8A8Srgb)
        {
            VkFormatProperties properties;
            device!.InstanceApi.vkGetPhysicalDeviceFormatProperties(device.PhysicalDevice, swapchainFormat, &properties);

            const VkFormatFeatureFlags needed = VkFormatFeatureFlags.ColorAttachment | VkFormatFeatureFlags.ColorAttachmentBlend
                                                                                   | VkFormatFeatureFlags.SampledImage | VkFormatFeatureFlags.SampledImageFilterLinear;

            if ((properties.optimalTilingFeatures & needed) == needed)
                format = swapchainFormat;
        }

        offscreenFormat = format;
        return format;
    }

    #endregion

    public void Dispose()
    {
        if (device == null)
            return;

        device.WaitIdle();

        // Releasing retires each one into the last frame, which the frame disposal below then destroys.
        IDisposable[] remaining;

        lock (liveResourcesLock)
            remaining = [.. liveResources];

        foreach (var resource in remaining)
            resource.Dispose();

        // Frames hand their staging buffers back as they go, so the pool is emptied after them.
        foreach (var frame in frames)
            frame?.Dispose();

        stagingPool?.Dispose();
        pipelines?.Dispose();
        descriptors?.Dispose();

        device.Api.vkDestroyBuffer(quadIndexBuffer, null);
        memory?.Free(quadIndexAllocation);
        device.Api.vkDestroyCommandPool(immediateCommandPool, null);

        swapchain?.Dispose();
        renderPasses?.Dispose();
        memory?.Dispose();
        device.Dispose();

        device = null;
        swapchain = null;
        renderPasses = null;
        windowSurface = null;
    }
}
