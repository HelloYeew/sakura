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
    /// Linear, trilinear between mip levels, repeating: how every regular texture is sampled, as on GL
    /// and Metal. Repeat because tiled fills rely on UVs past 1 wrapping around.
    /// </summary>
    public VkSampler TextureSampler { get; }

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

        VkSampler sampler;
        VulkanDevice.Check(api.vkCreateSampler(&samplerInfo, null, &sampler), "vkCreateSampler");
        TextureSampler = sampler;
        device.SetName(VkObjectType.Sampler, sampler.Handle, "Texture sampler (trilinear, repeat)");
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
        api.vkDestroySampler(TextureSampler, null);
        api.vkDestroyPipelineLayout(TextureArrayPipelineLayout, null);
        api.vkDestroyDescriptorSetLayout(TextureArrayLayout, null);
        api.vkDestroyDescriptorSetLayout(UniformLayout, null);
    }
}
