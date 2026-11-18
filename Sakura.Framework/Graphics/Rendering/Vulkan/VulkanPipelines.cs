// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Sakura.Framework.IO;
using Sakura.Framework.Logging;
using Sakura.Framework.Statistic;
using Vortice.Vulkan;
using SakuraVertex = Sakura.Framework.Graphics.Rendering.Vertex.Vertex;

namespace Sakura.Framework.Graphics.Rendering.Vulkan;

/// <summary>
/// Graphics pipelines, one per shader, blend mode and attachment format, created on first use or ahead
/// of it by <see cref="Prepare"/>. Every render pass of a format is compatible with every other (see
/// <see cref="VulkanRenderPasses"/>), so a pipeline draws into the swapchain and offscreen framebuffers alike.
/// </summary>
/// <remarks>
/// Compiling a pipeline is the slowest thing a Vulkan driver does, and a compile during a frame is a
/// visible hitch, so the driver's own cache of compiled pipelines is kept on disk between runs, and
/// <see cref="Prepare"/> compiles a shader's pipelines on a pool thread before they're drawn with.
/// The pipeline cache is internally synchronized, so both threads compile through it.
/// </remarks>
internal sealed unsafe class VulkanPipelines : IDisposable
{
    private static readonly GlobalStatistic<int> stat_pipeline_compiles = GlobalStatistics.Get<int>("Vulkan", "Pipeline Compiles", StatisticKind.Cumulative);

    /// <summary>
    /// The size of <c>VkPipelineCacheHeaderVersionOne</c>: header size, header version, vendor ID,
    /// device ID, and the 16-byte pipeline cache UUID.
    /// </summary>
    private const int cache_header_size = 32;

    private readonly VulkanDevice device;
    private readonly VulkanRenderPasses renderPasses;
    private readonly DiskCache? diskCache;
    private readonly string diskCacheKey;
    private readonly VkPipelineCache cache;
    private readonly Dictionary<(VulkanShader Shader, BlendingMode Blend, VkFormat Format), VkPipeline> pipelines = new Dictionary<(VulkanShader, BlendingMode, VkFormat), VkPipeline>();

    /// <summary>
    /// Pipelines <see cref="Prepare"/> compiled, waiting for the draw thread to take them in.
    /// </summary>
    private readonly ConcurrentQueue<((VulkanShader Shader, BlendingMode Blend, VkFormat Format) Key, VkPipeline Pipeline)> prepared = new ConcurrentQueue<((VulkanShader, BlendingMode, VkFormat), VkPipeline)>();

    /// <summary>
    /// The compile <see cref="Prepare"/> started for each shader, which releasing the shader waits for, since it reads
    /// the shader's modules.
    /// </summary>
    private readonly Dictionary<VulkanShader, Task> preparing = new Dictionary<VulkanShader, Task>();

    private bool cacheChanged;

    public VulkanPipelines(VulkanDevice device, VulkanRenderPasses renderPasses, DiskCache? diskCache)
    {
        this.device = device;
        this.renderPasses = renderPasses;
        this.diskCache = diskCache;

        var properties = device.Properties;
        diskCacheKey = $"vulkan-pipelines-{properties.vendorID:x4}-{properties.deviceID:x4}-{Convert.ToHexString(new ReadOnlySpan<byte>(properties.pipelineCacheUUID, 16))}-{properties.driverVersion:x8}";

        byte[]? initialData = loadCache();

        fixed (byte* data = initialData)
        {
            var createInfo = new VkPipelineCacheCreateInfo
            {
                initialDataSize = (nuint)(initialData?.Length ?? 0),
                pInitialData = data,
            };

            VkPipelineCache pipelineCache;
            VulkanDevice.Check(device.Api.vkCreatePipelineCache(&createInfo, null, &pipelineCache), "vkCreatePipelineCache");
            cache = pipelineCache;
        }
    }

    /// <summary>
    /// The pipeline that draws with <paramref name="shader"/> into an attachment of
    /// <paramref name="format"/> using <paramref name="blend"/>.
    /// </summary>
    public VkPipeline Get(VulkanShader shader, BlendingMode blend, VkFormat format)
    {
        if (!prepared.IsEmpty)
            takePrepared();

        if (pipelines.TryGetValue((shader, blend, format), out var pipeline))
            return pipeline;

        pipeline = create(shader, shader.VertexModule, shader.FragmentModule, blend, renderPasses.Get(format, VulkanPassKind.OffscreenClear));
        counted();
        pipelines[(shader, blend, format)] = pipeline;
        return pipeline;
    }

    /// <summary>
    /// Starts compiling, on a pool thread, every pipeline of <paramref name="shader"/> for the given attachment formats
    /// that isn't compiled yet, so its first draw in each blend mode doesn't wait for one. Does nothing for a shader
    /// already started.
    /// </summary>
    /// <remarks>
    /// A pipeline that fails here is left for its first use, which throws as it always has. One the draw thread needs
    /// before it's ready is compiled there as well, and the spare is destroyed.
    /// </remarks>
    public void Prepare(VulkanShader shader, IReadOnlyList<VkFormat> formats)
    {
        if (preparing.ContainsKey(shader))
            return;

        var work = new List<(BlendingMode Blend, VkFormat Format, VkRenderPass RenderPass)>();

        foreach (var format in formats)
        {
            // Render passes are cached unguarded, so they're looked up here on the draw thread.
            var renderPass = renderPasses.Get(format, VulkanPassKind.OffscreenClear);

            foreach (var blend in Enum.GetValues<BlendingMode>())
            {
                if (!pipelines.ContainsKey((shader, blend, format)))
                    work.Add((blend, format, renderPass));
            }
        }

        // Read here, since disposing the shader clears them before it waits for this compile.
        var vertex = shader.VertexModule;
        var fragment = shader.FragmentModule;

        if (work.Count > 0)
            preparing[shader] = Task.Run(() => prepare(shader, vertex, fragment, work));
    }

    /// <summary>
    /// Compiles <paramref name="work"/> on a pool thread, queueing each pipeline for the draw thread.
    /// </summary>
    private void prepare(VulkanShader shader, VkShaderModule vertex, VkShaderModule fragment, List<(BlendingMode Blend, VkFormat Format, VkRenderPass RenderPass)> work)
    {
        foreach (var (blend, format, renderPass) in work)
        {
            try
            {
                prepared.Enqueue(((shader, blend, format), create(shader, vertex, fragment, blend, renderPass)));
            }
            catch (InvalidOperationException e)
            {
                Logger.Verbose($"Vulkan: couldn't compile {shader.Name} ({blend}) ahead of use ({e.Message}); it will be tried again when it's first drawn.");
            }
        }
    }

    /// <summary>
    /// Keeps each pipeline <see cref="Prepare"/> has finished, or destroys it if the draw thread compiled the same one
    /// first (nothing has drawn with a spare).
    /// </summary>
    private void takePrepared()
    {
        while (prepared.TryDequeue(out var item))
        {
            counted();

            if (!pipelines.TryAdd(item.Key, item.Pipeline))
                device.Api.vkDestroyPipeline(item.Pipeline, null);
        }
    }

    /// <summary>
    /// Waits for every compile <see cref="Prepare"/> started, and takes in what they made.
    /// </summary>
    public void FinishPreparing()
    {
        foreach (var task in preparing.Values)
            wait(task);

        preparing.Clear();
        takePrepared();
    }

    /// <summary>
    /// Waits for the compile <see cref="Prepare"/> started for <paramref name="shader"/>, if one is still running, and
    /// takes in what it made.
    /// </summary>
    private void finishPreparing(VulkanShader shader)
    {
        if (preparing.Remove(shader, out var task))
            wait(task);

        takePrepared();
    }

    /// <summary>
    /// Waits for a compile <see cref="Prepare"/> started, whatever became of it.
    /// </summary>
    private static void wait(Task task)
    {
        try
        {
            task.Wait();
        }
        catch (AggregateException)
        {
            // Only a driver's own failures reach here; each pipeline is compiled again at its first use.
        }
    }

    /// <summary>
    /// Counts a pipeline compiled, and marks the cache as having something new to save.
    /// </summary>
    private void counted()
    {
        stat_pipeline_compiles.Value++;
        cacheChanged = true;
    }

    /// <summary>
    /// Removes every pipeline built from <paramref name="shader"/>, handing each to
    /// <paramref name="retire"/> to destroy once the GPU is done with it.
    /// </summary>
    public void Release(VulkanShader shader, Action<VkPipeline> retire)
    {
        finishPreparing(shader);

        var keys = new List<(VulkanShader, BlendingMode, VkFormat)>();

        foreach (var key in pipelines.Keys)
        {
            if (key.Shader == shader)
                keys.Add(key);
        }

        foreach (var key in keys)
        {
            retire(pipelines[key]);
            pipelines.Remove(key);
        }
    }

    /// <summary>
    /// Writes the driver's pipeline cache to disk if anything was compiled since the last save.
    /// </summary>
    public void SaveIfChanged()
    {
        if (!cacheChanged || diskCache == null)
            return;

        cacheChanged = false;

        try
        {
            nuint size;
            VulkanDevice.Check(device.Api.vkGetPipelineCacheData(cache, &size, null), "vkGetPipelineCacheData");

            byte[] data = new byte[(int)size];

            fixed (byte* p = data)
                VulkanDevice.Check(device.Api.vkGetPipelineCacheData(cache, &size, p), "vkGetPipelineCacheData");

            diskCache.Write(diskCacheKey, (int)size == data.Length ? data : data[..(int)size]);
        }
        catch (Exception e)
        {
            Logger.Verbose($"Vulkan: couldn't save the pipeline cache ({e.Message}); pipelines will compile again next run.");
        }
    }

    /// <summary>
    /// Assumes the device is idle.
    /// </summary>
    public void Dispose()
    {
        FinishPreparing();
        SaveIfChanged();

        foreach (var pipeline in pipelines.Values)
            device.Api.vkDestroyPipeline(pipeline, null);

        pipelines.Clear();
        device.Api.vkDestroyPipelineCache(cache, null);
    }

    /// <summary>
    /// Reads the cache saved by an earlier run, if its header says it came from this exact device and
    /// driver. Drivers are supposed to reject a foreign cache themselves, but some older ones (Android
    /// especially) crash on one instead, so it never reaches them unchecked.
    /// </summary>
    private byte[]? loadCache()
    {
        if (diskCache == null || !diskCache.TryRead(diskCacheKey, out byte[] data))
            return null;

        var properties = device.Properties;

        if (data.Length < cache_header_size
            || BitConverter.ToUInt32(data, 0) < cache_header_size
            || BitConverter.ToUInt32(data, 4) != (uint)VkPipelineCacheHeaderVersion.One
            || BitConverter.ToUInt32(data, 8) != properties.vendorID
            || BitConverter.ToUInt32(data, 12) != properties.deviceID
            || !data.AsSpan(16, 16).SequenceEqual(new ReadOnlySpan<byte>(properties.pipelineCacheUUID, 16)))
        {
            Logger.Verbose("Vulkan: ignoring a saved pipeline cache from a different device or driver.");
            return null;
        }

        return data;
    }

    /// <summary>
    /// Compiles one pipeline from <paramref name="shader"/>'s modules. Safe on any thread, given modules and a render
    /// pass read on the draw thread.
    /// </summary>
    /// <exception cref="InvalidOperationException">The driver couldn't compile it.</exception>
    private VkPipeline create(VulkanShader shader, VkShaderModule vertex, VkShaderModule fragment, BlendingMode blend, VkRenderPass renderPass)
    {
        byte* entryPoint = stackalloc byte[] { (byte)'m', (byte)'a', (byte)'i', (byte)'n', 0 };

        var stages = stackalloc VkPipelineShaderStageCreateInfo[2];
        stages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = vertex, pName = entryPoint };
        stages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = fragment, pName = entryPoint };

        var binding = new VkVertexInputBindingDescription((uint)SakuraVertex.Size, VkVertexInputRate.Vertex, 0);

        // Only the locations the shader reads: the vertex always carries all seven fields, but describing
        // ones nothing consumes is wasted work for the driver.
        var inputs = shader.Layout.VertexInputs;
        var attributes = stackalloc VkVertexInputAttributeDescription[Math.Max(1, inputs.Count)];

        for (int i = 0; i < inputs.Count; i++)
        {
            var (field, components) = VulkanShaderLayout.VertexAttributes[(int)inputs[i].Location];
            attributes[i] = new VkVertexInputAttributeDescription(inputs[i].Location, formatFor(components), (uint)Marshal.OffsetOf<SakuraVertex>(field), 0);
        }

        var vertexInput = new VkPipelineVertexInputStateCreateInfo
        {
            vertexBindingDescriptionCount = 1,
            pVertexBindingDescriptions = &binding,
            vertexAttributeDescriptionCount = (uint)inputs.Count,
            pVertexAttributeDescriptions = attributes,
        };

        var inputAssembly = new VkPipelineInputAssemblyStateCreateInfo { topology = VkPrimitiveTopology.TriangleList };

        // Viewport and scissor are dynamic, so one pipeline serves every target size.
        var viewport = new VkPipelineViewportStateCreateInfo { viewportCount = 1, scissorCount = 1 };

        var rasterization = new VkPipelineRasterizationStateCreateInfo
        {
            polygonMode = VkPolygonMode.Fill,
            cullMode = VkCullModeFlags.None,
            frontFace = VkFrontFace.CounterClockwise,
            lineWidth = 1f,
        };

        var multisample = new VkPipelineMultisampleStateCreateInfo { rasterizationSamples = VkSampleCountFlags.Count1 };

        var blendAttachment = blendStateFor(blend);

        var colorBlend = new VkPipelineColorBlendStateCreateInfo
        {
            attachmentCount = 1,
            pAttachments = &blendAttachment,
        };

        var dynamicStates = stackalloc VkDynamicState[] { VkDynamicState.Viewport, VkDynamicState.Scissor };
        var dynamic = new VkPipelineDynamicStateCreateInfo { dynamicStateCount = 2, pDynamicStates = dynamicStates };

        // No depth/stencil state: no pass has a depth or stencil attachment.
        var createInfo = new VkGraphicsPipelineCreateInfo
        {
            stageCount = 2,
            pStages = stages,
            pVertexInputState = &vertexInput,
            pInputAssemblyState = &inputAssembly,
            pViewportState = &viewport,
            pRasterizationState = &rasterization,
            pMultisampleState = &multisample,
            pColorBlendState = &colorBlend,
            pDynamicState = &dynamic,
            layout = shader.PipelineLayout,
            renderPass = renderPass,
            subpass = 0,
        };

        VkPipeline pipeline;
        VulkanDevice.Check(device.Api.vkCreateGraphicsPipelines(cache, 1, &createInfo, null, &pipeline), "vkCreateGraphicsPipelines");
        device.SetName(VkObjectType.Pipeline, pipeline.Handle, $"{shader.Name} ({blend})");
        return pipeline;
    }

    private static VkFormat formatFor(uint components) => components switch
    {
        1 => VkFormat.R32Sfloat,
        2 => VkFormat.R32G32Sfloat,
        3 => VkFormat.R32G32B32Sfloat,
        _ => VkFormat.R32G32B32A32Sfloat,
    };

    /// <summary>
    /// The blend factors of each mode, copied from <c>GLRenderer.applyBlend</c>. The alpha factors are
    /// what an offscreen buffer accumulates and BufferedContainer's premultiplied composite reads back,
    /// so they follow GL rather than the Metal and Direct3D 11 renderers, which differ there.
    /// </summary>
    private static VkPipelineColorBlendAttachmentState blendStateFor(BlendingMode blend)
    {
        var (srcColor, dstColor, srcAlpha, dstAlpha) = blend switch
        {
            BlendingMode.Additive => (VkBlendFactor.SrcAlpha, VkBlendFactor.One, VkBlendFactor.Zero, VkBlendFactor.One),
            BlendingMode.Opaque => (VkBlendFactor.One, VkBlendFactor.Zero, VkBlendFactor.One, VkBlendFactor.Zero),
            BlendingMode.Multiply => (VkBlendFactor.DstColor, VkBlendFactor.OneMinusSrcAlpha, VkBlendFactor.One, VkBlendFactor.OneMinusSrcAlpha),
            BlendingMode.Screen => (VkBlendFactor.One, VkBlendFactor.OneMinusSrcColor, VkBlendFactor.One, VkBlendFactor.OneMinusSrcAlpha),
            BlendingMode.Premultiplied => (VkBlendFactor.One, VkBlendFactor.OneMinusSrcAlpha, VkBlendFactor.One, VkBlendFactor.OneMinusSrcAlpha),
            _ => (VkBlendFactor.SrcAlpha, VkBlendFactor.OneMinusSrcAlpha, VkBlendFactor.One, VkBlendFactor.OneMinusSrcAlpha),
        };

        return new VkPipelineColorBlendAttachmentState(true, srcColor, dstColor, VkBlendOp.Add, srcAlpha, dstAlpha, VkBlendOp.Add, VkColorComponentFlags.All);
    }
}
