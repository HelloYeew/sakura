// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Textures;
using Vortice.Vulkan;

namespace Sakura.Framework.Graphics.Rendering.Vulkan;

/// <summary>
/// An offscreen render target: a color texture and the <c>VkFramebuffer</c> that renders into it.
/// </summary>
/// <remarks>
/// A resize replaces both. The old ones are only retired, not destroyed, since draws already recorded
/// this frame (or still running from the last) may sample or render into them; they go once the GPU has
/// finished those.
/// </remarks>
public sealed unsafe class VulkanFrameBuffer : IFrameBuffer
{
    private readonly VulkanRenderer renderer;
    private readonly VulkanDevice device;
    private readonly VulkanMemory memory;
    private readonly VulkanRenderPasses renderPasses;
    private readonly VkSampler sampler;

    private VulkanTexture? colorTexture;
    private VkFramebuffer framebuffer;

    public Texture Texture { get; private set; } = null!;
    public int Width { get; private set; }
    public int Height { get; private set; }

    internal VkFormat Format { get; }
    internal VulkanTexture ColorTexture => colorTexture!;
    internal VkFramebuffer Handle => framebuffer;

    internal VulkanFrameBuffer(VulkanRenderer renderer, VulkanDevice device, VulkanMemory memory, VulkanRenderPasses renderPasses, VkFormat format, VkSampler sampler, int width, int height)
    {
        this.renderer = renderer;
        this.device = device;
        this.memory = memory;
        this.renderPasses = renderPasses;
        this.sampler = sampler;
        Format = format;

        createAttachment(width, height);
    }

    public void Resize(int width, int height)
    {
        if (Math.Max(1, width) == Width && Math.Max(1, height) == Height)
            return;

        createAttachment(width, height);
    }

    /// <summary>
    /// Releases the color texture and framebuffer. Draw thread only.
    /// </summary>
    public void Dispose() => releaseAttachment();

    private void createAttachment(int width, int height)
    {
        releaseAttachment();

        Width = Math.Max(1, width);
        Height = Math.Max(1, height);

        colorTexture = VulkanTexture.CreateRenderTarget(renderer, device, memory, Width, Height, sampler, Format);

        var view = colorTexture.View;

        // Created against the clearing pass, and used with the loading one too: the two are compatible.
        var createInfo = new VkFramebufferCreateInfo
        {
            renderPass = renderPasses.Get(Format, VulkanPassKind.OffscreenClear),
            attachmentCount = 1,
            pAttachments = &view,
            width = (uint)Width,
            height = (uint)Height,
            layers = 1,
        };

        VkFramebuffer created;
        VulkanDevice.Check(device.Api.vkCreateFramebuffer(&createInfo, null, &created), "vkCreateFramebuffer");
        framebuffer = created;

        // The wrapper owns the color texture, so disposing it (and only it) releases the texture and
        // unregisters it from the texture registry.
        Texture = new Texture(colorTexture);
    }

    private void releaseAttachment()
    {
        if (colorTexture == null)
            return;

        Texture.Dispose();
        colorTexture = null;

        var released = framebuffer;
        framebuffer = VkFramebuffer.Null;
        renderer.Retire(() => device.Api.vkDestroyFramebuffer(released, null));
    }
}
