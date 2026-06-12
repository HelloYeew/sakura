// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Logging;
using Silk.NET.OpenGL;
using Texture = Sakura.Framework.Graphics.Textures.Texture;

namespace Sakura.Framework.Graphics.Rendering;

/// <summary>
/// The OpenGL implementation of <see cref="IFrameBuffer"/>: a GL framebuffer object with a
/// single color attachment. Masking in this framework is shader-based, so no depth/stencil
/// attachment is needed.
/// </summary>
public class GLFrameBuffer : IFrameBuffer
{
    private readonly GL gl;

    /// <summary>
    /// The raw GL framebuffer object handle.
    /// </summary>
    internal uint Handle { get; private set; }

    private GLTexture colorTexture;
    private readonly bool pixelSnapping;

    public Texture Texture { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>
    /// Must be created on the draw thread.
    /// </summary>
    public GLFrameBuffer(GL gl, int width, int height, bool pixelSnapping = false)
    {
        this.gl = gl;
        this.pixelSnapping = pixelSnapping;
        Handle = gl.GenFramebuffer();
        createAttachment(width, height);
    }

    private void createAttachment(int width, int height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);

        // Creation can happen while another framebuffer (or the default one) is bound
        // mid-frame, so the previous binding must be preserved.
        gl.GetInteger(GLEnum.FramebufferBinding, out int previousBinding);

        colorTexture?.Dispose();
        colorTexture = GLTexture.CreateRenderTarget(gl, Width, Height, pixelSnapping);
        Texture = new Texture(colorTexture);

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, Handle);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, colorTexture.GLHandle, 0);

        var status = gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
            Logger.Error($"Framebuffer incomplete: {status} ({Width}x{Height})");

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)previousBinding);
    }

    public void Resize(int width, int height)
    {
        if (width == Width && height == Height)
            return;

        createAttachment(width, height);
    }

    public void Dispose()
    {
        colorTexture?.Dispose();
        colorTexture = null;

        if (Handle != 0)
        {
            gl.DeleteFramebuffer(Handle);
            Handle = 0;
        }
    }
}
