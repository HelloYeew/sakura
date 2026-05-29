// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// A public-facing texture that drawables use.
/// Points to a specific region (UvRect) within a larger GLTexture (atlas or standalone),
/// or wraps a <see cref="VideoGLTexture"/> for the video pipeline.
/// </summary>
public class Texture : IDisposable
{
    /// <summary>
    /// The underlying GPU texture (for regular images). Null for video textures.
    /// </summary>
    public GLTexture? GlTexture { get; }

    /// <summary>
    /// The underlying YUV GPU texture (for video). Null for regular textures.
    /// </summary>
    public VideoGLTexture? VideoGlTexture { get; }

    /// <summary>
    /// Whether this is a YUV video texture.
    /// </summary>
    public bool IsVideoTexture => VideoGlTexture != null;

    /// <summary>
    /// The rectangle (in 0-1 UV coordinates) this texture occupies within its GLTexture.
    /// Always (0,0,1,1) for video and standalone textures.
    /// </summary>
    public RectangleF UvRect { get; }

    public int Width { get; }
    public int Height { get; }

    /// <summary>
    /// Whether this texture has valid uploaded data and is safe to render.
    /// For video textures this reflects <see cref="VideoGLTexture.Available"/>.
    /// </summary>
    public bool IsAvailable => VideoGlTexture?.Available ?? GlTexture?.Available ?? false;

    /// <summary>
    /// Creates a texture wrapping the entire area of a regular GLTexture.
    /// </summary>
    public Texture(GLTexture glTexture)
    {
        GlTexture = glTexture;
        UvRect = new RectangleF(0, 0, 1, 1);
        Width = glTexture.Width;
        Height = glTexture.Height;
    }

    /// <summary>
    /// Creates a texture wrapping a sub-region of a regular GLTexture.
    /// </summary>
    public Texture(GLTexture glTexture, RectangleF uvRect)
    {
        GlTexture = glTexture;
        UvRect = uvRect;
        Width = (int)(glTexture.Width * uvRect.Width);
        Height = (int)(glTexture.Height * uvRect.Height);
    }

    /// <summary>
    /// Creates a texture wrapping a three-plane YUV video texture.
    /// </summary>
    public Texture(VideoGLTexture videoGlTexture)
    {
        VideoGlTexture = videoGlTexture;
        UvRect = new RectangleF(0, 0, 1, 1);
        Width = videoGlTexture.Width;
        Height = videoGlTexture.Height;
    }

    public void Dispose()
    {
        GlTexture?.Dispose();
        VideoGlTexture?.Dispose();
    }
}
