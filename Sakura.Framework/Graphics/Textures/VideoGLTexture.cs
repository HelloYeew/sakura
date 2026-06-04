// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Sakura.Framework.Graphics.Video;
using Silk.NET.OpenGL;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// Holds three single-channel GL textures representing the Y, U, V planes of a YUV420P frame.
/// Used exclusively by the GL video pipeline — the video shader samples all three planes and
/// performs YUV→RGB conversion on the GPU.
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
public sealed class VideoGLTexture : IDisposable
{
    public uint YHandle { get; private set; }
    public uint UHandle { get; private set; }
    public uint VHandle { get; private set; }

    /// <summary>
    /// Whether the texture has been uploaded at least once and is ready to draw.
    /// Written on the draw thread, read on the update thread — must use Volatile.
    /// </summary>
    public bool Available => Volatile.Read(ref available);
    private bool available;

    public int Width { get; }
    public int Height { get; }

    private readonly GL gl;
    private bool disposed;

    public VideoGLTexture(GL gl, int width, int height)
    {
        this.gl = gl;
        Width = width;
        Height = height;

        YHandle = gl.GenTexture();
        UHandle = gl.GenTexture();
        VHandle = gl.GenTexture();
    }

    /// <summary>
    /// Binds the Y, U, V planes to texture units 0, 1, 2 respectively.
    /// Must be called on the draw thread. Keeps GL calls inside this layer
    /// so higher-level code (VideoDrawNode) doesn't need a GL reference.
    /// </summary>
    public void BindPlanes()
    {
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(TextureTarget.Texture2D, YHandle);

        gl.ActiveTexture(TextureUnit.Texture1);
        gl.BindTexture(TextureTarget.Texture2D, UHandle);

        gl.ActiveTexture(TextureUnit.Texture2);
        gl.BindTexture(TextureTarget.Texture2D, VHandle);
    }

    /// <summary>
    /// Marks the texture as having valid data. Called by <see cref="VideoTextureUpload"/> after upload.
    /// </summary>
    public void MarkAvailable() => Volatile.Write(ref available, true);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        gl.DeleteTexture(YHandle);
        gl.DeleteTexture(UHandle);
        gl.DeleteTexture(VHandle);

        GC.SuppressFinalize(this);
    }
}
