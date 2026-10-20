// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Vortice.Vulkan;

namespace Sakura.Framework.Graphics.Rendering.Vulkan;

/// <summary>
/// The descriptor set layouts and pipeline layouts every pipeline shares (see
/// <see cref="VulkanShaderLayout"/> for the layout itself), and the samplers textures are read with.
/// </summary>
internal sealed unsafe class VulkanDescriptors : IDisposable
{
    private readonly VulkanDevice device;

    /// <summary>
    /// Set 0: the uniform blocks, all dynamic uniform buffers into the frame's stream buffer.
    /// </summary>
    public VkDescriptorSetLayout UniformLayout { get; }

    /// <summary>
    /// Set 1 for shaders with a texture array: one array of <see cref="VulkanShaderLayout.TEXTURE_ARRAY_LENGTH"/>
    /// combined image samplers at binding 0.
    /// </summary>
    public VkDescriptorSetLayout TextureArrayLayout { get; }

    public VkPipelineLayout TextureArrayPipelineLayout { get; }

    /// <summary>
    /// Set 1 for shaders with single textures (effects, video, most game shaders): one combined image
    /// sampler at each of bindings 0 to <see cref="VulkanShaderLayout.SINGLE_TEXTURE_COUNT"/> - 1.
    /// </summary>
    public VkDescriptorSetLayout SingleTextureLayout { get; }

    public VkPipelineLayout SingleTexturePipelineLayout { get; }

    /// <summary>
    /// Linear, trilinear between mip levels, repeating: how every regular texture is sampled, as on GL
    /// and Metal. Repeat because tiled fills rely on UVs past 1 wrapping around.
    /// </summary>
    public VkSampler TextureSampler { get; }

    /// <summary>
    /// Linear, clamped to the edge, no mip levels: how offscreen framebuffers are sampled. Clamped
    /// because the blur reads past the buffer's edges and must see the edge texels there, not the
    /// opposite side.
    /// </summary>
    public VkSampler RenderTargetSampler { get; }

    /// <summary>
    /// Nearest, clamped to the edge: framebuffers created with pixel snapping, which composite whole
    /// texels instead of smoothing between them.
    /// </summary>
    public VkSampler PixelSnappingSampler { get; }

    public VulkanDescriptors(VulkanDevice device)
    {
        this.device = device;
        var api = device.Api;

        var uniformBindings = stackalloc VkDescriptorSetLayoutBinding[(int)VulkanShaderLayout.UNIFORM_BINDING_COUNT];

        for (uint i = 0; i < VulkanShaderLayout.UNIFORM_BINDING_COUNT; i++)
        {
            uniformBindings[i] = new VkDescriptorSetLayoutBinding
            {
                binding = i,
                descriptorType = VkDescriptorType.UniformBufferDynamic,
                descriptorCount = 1,
                stageFlags = VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment,
            };
        }

        UniformLayout = createSetLayout(uniformBindings, VulkanShaderLayout.UNIFORM_BINDING_COUNT, "Uniform blocks (set 0)");

        var textureArrayBinding = new VkDescriptorSetLayoutBinding
        {
            binding = 0,
            descriptorType = VkDescriptorType.CombinedImageSampler,
            descriptorCount = VulkanShaderLayout.TEXTURE_ARRAY_LENGTH,
            stageFlags = VkShaderStageFlags.Fragment,
        };

        TextureArrayLayout = createSetLayout(&textureArrayBinding, 1, "Texture array (set 1)");

        var setLayouts = stackalloc VkDescriptorSetLayout[] { UniformLayout, TextureArrayLayout };

        var pipelineLayoutInfo = new VkPipelineLayoutCreateInfo
        {
            setLayoutCount = 2,
            pSetLayouts = setLayouts,
        };

        VkPipelineLayout pipelineLayout;
        VulkanDevice.Check(api.vkCreatePipelineLayout(&pipelineLayoutInfo, null, &pipelineLayout), "vkCreatePipelineLayout");
        TextureArrayPipelineLayout = pipelineLayout;
        device.SetName(VkObjectType.PipelineLayout, pipelineLayout.Handle, "Texture array pipeline layout");

        var singleBindings = stackalloc VkDescriptorSetLayoutBinding[(int)VulkanShaderLayout.SINGLE_TEXTURE_COUNT];

        for (uint i = 0; i < VulkanShaderLayout.SINGLE_TEXTURE_COUNT; i++)
        {
            singleBindings[i] = new VkDescriptorSetLayoutBinding
            {
                binding = i,
                descriptorType = VkDescriptorType.CombinedImageSampler,
                descriptorCount = 1,
                stageFlags = VkShaderStageFlags.Fragment,
            };
        }

        SingleTextureLayout = createSetLayout(singleBindings, VulkanShaderLayout.SINGLE_TEXTURE_COUNT, "Single textures (set 1)");

        // Set 0 is the same layout in both pipeline layouts, so it stays bound across a switch between
        // a texture-array shader and a single-texture one.
        setLayouts[1] = SingleTextureLayout;
        VulkanDevice.Check(api.vkCreatePipelineLayout(&pipelineLayoutInfo, null, &pipelineLayout), "vkCreatePipelineLayout");
        SingleTexturePipelineLayout = pipelineLayout;
        device.SetName(VkObjectType.PipelineLayout, pipelineLayout.Handle, "Single texture pipeline layout");

        var samplerInfo = new VkSamplerCreateInfo
        {
            magFilter = VkFilter.Linear,
            minFilter = VkFilter.Linear,
            mipmapMode = VkSamplerMipmapMode.Linear,
            addressModeU = VkSamplerAddressMode.Repeat,
            addressModeV = VkSamplerAddressMode.Repeat,
            addressModeW = VkSamplerAddressMode.Repeat,
            minLod = 0,
            // VK_LOD_CLAMP_NONE: every mip level the texture has.
            maxLod = 1000f,
            borderColor = VkBorderColor.FloatTransparentBlack,
        };

        TextureSampler = createSampler(samplerInfo, "Texture sampler (trilinear, repeat)");

        samplerInfo.mipmapMode = VkSamplerMipmapMode.Nearest;
        samplerInfo.addressModeU = VkSamplerAddressMode.ClampToEdge;
        samplerInfo.addressModeV = VkSamplerAddressMode.ClampToEdge;
        samplerInfo.addressModeW = VkSamplerAddressMode.ClampToEdge;
        samplerInfo.maxLod = 0f;
        RenderTargetSampler = createSampler(samplerInfo, "Render target sampler (linear, clamp)");

        samplerInfo.magFilter = VkFilter.Nearest;
        samplerInfo.minFilter = VkFilter.Nearest;
        PixelSnappingSampler = createSampler(samplerInfo, "Pixel snapping sampler (nearest, clamp)");
    }

    /// <summary>
    /// Allocates a set 0 for <paramref name="frame"/> whose every binding reads
    /// <see cref="VulkanShaderLayout.UNIFORM_BLOCK_RANGE"/> bytes of <paramref name="streamBuffer"/>.
    /// Where in the buffer is chosen per draw, by dynamic offsets.
    /// </summary>
    public VkDescriptorSet AllocateUniformSet(VulkanFrameContext frame, VkBuffer streamBuffer)
    {
        var set = frame.AllocateDescriptorSet(UniformLayout);

        var bufferInfo = new VkDescriptorBufferInfo
        {
            buffer = streamBuffer,
            offset = 0,
            range = VulkanShaderLayout.UNIFORM_BLOCK_RANGE,
        };

        var writes = stackalloc VkWriteDescriptorSet[(int)VulkanShaderLayout.UNIFORM_BINDING_COUNT];

        for (uint i = 0; i < VulkanShaderLayout.UNIFORM_BINDING_COUNT; i++)
        {
            writes[i] = new VkWriteDescriptorSet
            {
                dstSet = set,
                dstBinding = i,
                descriptorCount = 1,
                descriptorType = VkDescriptorType.UniformBufferDynamic,
                pBufferInfo = &bufferInfo,
            };
        }

        device.Api.vkUpdateDescriptorSets(VulkanShaderLayout.UNIFORM_BINDING_COUNT, writes, 0, null);
        return set;
    }

    /// <summary>
    /// Allocates a set 1 for <paramref name="frame"/> holding <paramref name="views"/> at elements 0 and
    /// up of the texture array.
    /// </summary>
    public VkDescriptorSet AllocateTextureArraySet(VulkanFrameContext frame, ReadOnlySpan<VkImageView> views, ReadOnlySpan<VkSampler> samplers)
    {
        var set = frame.AllocateDescriptorSet(TextureArrayLayout);

        var imageInfos = stackalloc VkDescriptorImageInfo[views.Length];

        for (int i = 0; i < views.Length; i++)
        {
            imageInfos[i] = new VkDescriptorImageInfo
            {
                sampler = samplers[i],
                imageView = views[i],
                imageLayout = VkImageLayout.ShaderReadOnlyOptimal,
            };
        }

        var write = new VkWriteDescriptorSet
        {
            dstSet = set,
            dstBinding = 0,
            dstArrayElement = 0,
            descriptorCount = (uint)views.Length,
            descriptorType = VkDescriptorType.CombinedImageSampler,
            pImageInfo = imageInfos,
        };

        device.Api.vkUpdateDescriptorSets(1, &write, 0, null);
        return set;
    }

    /// <summary>
    /// Allocates a set 1 for <paramref name="frame"/> holding <paramref name="views"/> at bindings 0 and
    /// up.
    /// </summary>
    public VkDescriptorSet AllocateSingleTextureSet(VulkanFrameContext frame, ReadOnlySpan<VkImageView> views, ReadOnlySpan<VkSampler> samplers)
    {
        var set = frame.AllocateDescriptorSet(SingleTextureLayout);

        var imageInfos = stackalloc VkDescriptorImageInfo[views.Length];
        var writes = stackalloc VkWriteDescriptorSet[views.Length];

        for (int i = 0; i < views.Length; i++)
        {
            imageInfos[i] = new VkDescriptorImageInfo
            {
                sampler = samplers[i],
                imageView = views[i],
                imageLayout = VkImageLayout.ShaderReadOnlyOptimal,
            };

            writes[i] = new VkWriteDescriptorSet
            {
                dstSet = set,
                dstBinding = (uint)i,
                descriptorCount = 1,
                descriptorType = VkDescriptorType.CombinedImageSampler,
                pImageInfo = &imageInfos[i],
            };
        }

        device.Api.vkUpdateDescriptorSets((uint)views.Length, writes, 0, null);
        return set;
    }

    private VkSampler createSampler(VkSamplerCreateInfo createInfo, string name)
    {
        VkSampler sampler;
        VulkanDevice.Check(device.Api.vkCreateSampler(&createInfo, null, &sampler), "vkCreateSampler");
        device.SetName(VkObjectType.Sampler, sampler.Handle, name);
        return sampler;
    }

    private VkDescriptorSetLayout createSetLayout(VkDescriptorSetLayoutBinding* bindings, uint count, string name)
    {
        var createInfo = new VkDescriptorSetLayoutCreateInfo
        {
            bindingCount = count,
            pBindings = bindings,
        };

        VkDescriptorSetLayout layout;
        VulkanDevice.Check(device.Api.vkCreateDescriptorSetLayout(&createInfo, null, &layout), "vkCreateDescriptorSetLayout");
        device.SetName(VkObjectType.DescriptorSetLayout, layout.Handle, name);
        return layout;
    }

    /// <summary>
    /// Assumes the device is idle.
    /// </summary>
    public void Dispose()
    {
        var api = device.Api;
        api.vkDestroySampler(PixelSnappingSampler, null);
        api.vkDestroySampler(RenderTargetSampler, null);
        api.vkDestroySampler(TextureSampler, null);
        api.vkDestroyPipelineLayout(SingleTexturePipelineLayout, null);
        api.vkDestroyDescriptorSetLayout(SingleTextureLayout, null);
        api.vkDestroyPipelineLayout(TextureArrayPipelineLayout, null);
        api.vkDestroyDescriptorSetLayout(TextureArrayLayout, null);
        api.vkDestroyDescriptorSetLayout(UniformLayout, null);
    }
}
