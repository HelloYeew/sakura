// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// A public-facing texture represents that drawable use.
/// It points to a specifix region (UvRect) within a larger TextureGL (atlas or standalone textures).
/// </summary>
public class Texture
{
    /// <summary>
    /// The underlying GPU texture.
    /// </summary>
    public GLTexture GlTexture { get; }

    /// <summary>
    /// The rectangle (in 0-1 UV coordinates) this texture occupies within its TextureGL.
    /// </summary>
    public RectangleF UvRect { get; }

    /// <summary>
    /// The width of this texture region in pixels.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// The height of this texture region in pixels.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Creates a new texture that represents the *entire* area of a TextureGL.
    /// </summary>
    public Texture(GLTexture glTexture)
    {
        GlTexture = glTexture;
        UvRect = new RectangleF(0, 0, 1, 1);
        Width = glTexture.Width;
        Height = glTexture.Height;
    }

    /// <summary>
    /// Creates a new texture that represents a *sub-region* of a TextureGL.
    /// </summary>
    public Texture(GLTexture glTexture, RectangleF uvRect)
    {
        GlTexture = glTexture;
        UvRect = uvRect;

        // Calculate pixel size of this specific region
        Width = (int)(glTexture.Width * uvRect.Width);
        Height = (int)(glTexture.Height * uvRect.Height);
    }
}
