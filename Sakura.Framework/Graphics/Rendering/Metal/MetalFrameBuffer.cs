// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Textures;

namespace Sakura.Framework.Graphics.Rendering.Metal;

/// <summary>
/// The Metal implementation of <see cref="IFrameBuffer"/>
/// </summary>
public sealed class MetalFrameBuffer : IFrameBuffer
{
    private readonly nint device; // SakuraMetalDevice*

    private MetalTexture colorTexture;

    public Texture Texture { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>
    /// The native render-target texture handle (MTLTexture*), used by the renderer to begin an
    /// offscreen pass into this buffer.
    /// </summary>
    internal nint TextureHandle => colorTexture.Handle;

    public MetalFrameBuffer(nint device, int width, int height)
    {
        this.device = device;
        createAttachment(width, height);
    }

    private void createAttachment(int width, int height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);

        releaseAttachment();

        colorTexture = MetalTexture.CreateRenderTarget(device, Width, Height);
        Texture = new Texture(colorTexture);
    }

    /// <summary>
    /// Releases the current colour attachment, if any.
    /// </summary>
    /// <remarks>
    /// Disposes the <see cref="Texture"/> wrapper rather than the backend it wraps. Only
    /// <c>Texture.Dispose</c> unregisters from <see cref="TextureRegistry"/>, and it destroys the GPU
    /// resource on the way (the wrapper owns it). Reaching past it to the backend frees the memory but
    /// leaves the wrapper registered for the process lifetime — which is what made resizing a window
    /// report a thousand live framebuffers, each still counted in <c>Live Bytes</c>, every one of them
    /// drawn by the viewer as a white card labelled "GPU texture destroyed".
    /// </remarks>
    private void releaseAttachment()
    {
        Texture?.Dispose();
        Texture = null;
        colorTexture = null;
    }

    public void Resize(int width, int height)
    {
        if (width == Width && height == Height)
            return;

        createAttachment(width, height);
    }

    /// <summary>
    /// Releases the colour attachment. Must be called on the draw thread.
    /// </summary>
    /// <remarks>
    /// Deliberately has no finalizer: this type owns no native handle of its own (a Metal render pass
    /// is configured per frame rather than baked into a framebuffer object), so the only native
    /// resource here is <c>colorTexture</c> — a <see cref="MetalTexture"/>, which carries its own
    /// finalizer safety net. Reaching into it from a finalizer here would be unsafe anyway, since
    /// finalization order is undefined.
    /// </remarks>
    public void Dispose() => releaseAttachment();
}
