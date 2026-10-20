// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Maths;
using Vortice.Vulkan;

namespace Sakura.Framework.Graphics.Rendering.Vulkan;

/// <summary>
/// A vertex + fragment shader pair compiled to SPIR-V, checked against the renderer's resource layout.
/// </summary>
/// <remarks>
/// Uniforms only travel in blocks, as on Direct3D 11: the scalar <c>SetUniform</c> overloads do
/// nothing, and <see cref="SetUniformBlock{T}"/> finds the block's binding by the name the shader
/// declares it with.
/// </remarks>
public sealed unsafe class VulkanShader : IShader
{
    private readonly VulkanRenderer renderer;
    private readonly VulkanDevice device;

    internal string Name { get; }
    internal VulkanShaderLayout Layout { get; }
    internal VkPipelineLayout PipelineLayout { get; }
    internal VkShaderModule VertexModule { get; private set; }
    internal VkShaderModule FragmentModule { get; private set; }

    public nint Handle => (nint)VertexModule.Handle;

    /// <exception cref="InvalidOperationException">The shader doesn't fit the resource layout. The
    /// message names every problem.</exception>
    internal VulkanShader(VulkanRenderer renderer, VulkanDevice device, SpirvShaderPair spirv, string name, VulkanDescriptors descriptors)
    {
        this.renderer = renderer;
        this.device = device;
        Name = name;

        Layout = VulkanShaderLayout.Create(SpirvReflection.Parse(spirv.VertexForReflection), SpirvReflection.Parse(spirv.FragmentForReflection), name);

        PipelineLayout = Layout.UsesTextureArray ? descriptors.TextureArrayPipelineLayout : descriptors.SingleTexturePipelineLayout;

        VertexModule = createModule(spirv.Vertex, $"{name} (vertex)");
        FragmentModule = createModule(spirv.Fragment, $"{name} (fragment)");
    }

    public void Use() => renderer.UseShader(this);

    public void SetUniformBlock<T>(string blockName, in T data) where T : unmanaged
    {
        // A block the shader doesn't declare (or one the compiler removed as unused) has nowhere to go.
        if (Layout.UniformBlocks.TryGetValue(blockName, out var block))
            renderer.SetUniformBlock(block.Binding, in data);
    }

    public void SetUniform(string name, int value)
    {
    }

    public void SetUniform(string name, float value)
    {
    }

    public void SetUniform(string name, bool value)
    {
    }

    public void SetUniform(string name, Matrix4x4 value)
    {
    }

    public void SetUniform(string name, Vector2 value)
    {
    }

    public void SetUniform(string name, Vector4 value)
    {
    }

    public void SetUniform(string name, Color value)
    {
    }

    public void SetUniformIntArray(string name, int[] values)
    {
    }

    public void SetUniform(string name, float[] mat3X3)
    {
    }

    /// <summary>
    /// Releases the modules and every pipeline built from them, once the GPU can no longer be using
    /// them. Draw thread only.
    /// </summary>
    public void Dispose()
    {
        if (VertexModule.IsNull)
            return;

        var vertex = VertexModule;
        var fragment = FragmentModule;
        VertexModule = VkShaderModule.Null;
        FragmentModule = VkShaderModule.Null;

        renderer.ReleaseShader(this, () =>
        {
            device.Api.vkDestroyShaderModule(vertex, null);
            device.Api.vkDestroyShaderModule(fragment, null);
        });
    }

    private VkShaderModule createModule(byte[] code, string name)
    {
        fixed (byte* p = code)
        {
            var createInfo = new VkShaderModuleCreateInfo
            {
                codeSize = (nuint)code.Length,
                pCode = (uint*)p,
            };

            VkShaderModule module;
            VulkanDevice.Check(device.Api.vkCreateShaderModule(&createInfo, null, &module), "vkCreateShaderModule");
            device.SetName(VkObjectType.ShaderModule, module.Handle, name);
            return module;
        }
    }
}
