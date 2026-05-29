// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Video;
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
    /// The <see cref="IVideoTexture"/> that manages upload lifecycle for this video texture.
    /// Null for regular textures.
    /// </summary>
    public IVideoTexture? VideoTexture { get; }

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
    /// For video textures this reflects <see cref="IVideoTexture.UploadComplete"/>.
    /// </summary>
    public bool IsAvailable => VideoTexture?.UploadComplete ?? GlTexture?.Available ?? false;

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

    /// <summary>
    /// Creates a video texture that carries both the GL handles and the upload-lifecycle manager.
    /// Used by <see cref="VideoDecoder"/> so <see cref="VideoSprite"/> can poll
    /// <see cref="IVideoTexture.UploadComplete"/> without knowing the concrete type.
    /// </summary>
    public Texture(VideoGLTexture videoGlTexture, IVideoTexture videoTexture)
    {
        VideoGlTexture = videoGlTexture;
        VideoTexture = videoTexture;
        UvRect = new RectangleF(0, 0, 1, 1);
        Width = videoGlTexture.Width;
        Height = videoGlTexture.Height;
    }

    public void Dispose()
    {
        GlTexture?.Dispose();
    }
}
