// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// A public-facing texture that drawables use.
/// Points to a specific region (UvRect) within a larger <see cref="INativeTexture"/>
/// (atlas or standalone).
/// </summary>
public class Texture : IDisposable
{
    /// <summary>
    /// The underlying GPU texture. Null for dimension-only proxy textures.
    /// </summary>
    public INativeTexture? BackendTexture { get; }

    /// <summary>
    /// UV region within the native texture (0–1 coordinates).
    /// </summary>
    public RectangleF UvRect { get; }

    public int Width { get; }
    public int Height { get; }

    /// <summary>
    /// True once the GPU upload has completed and the texture is safe to render.
    /// </summary>
    public bool IsAvailable => BackendTexture?.Available ?? false;

    /// <summary>
    /// Creates a texture wrapping the entire area of a <see cref="INativeTexture"/>.
    /// </summary>
    public Texture(INativeTexture backendTexture)
    {
        BackendTexture = backendTexture;
        UvRect = new RectangleF(0, 0, 1, 1);
        Width = backendTexture.Width;
        Height = backendTexture.Height;
    }

    /// <summary>
    /// Creates a texture wrapping a sub-region of a <see cref="INativeTexture"/>.
    /// Used by <see cref="TextureAtlas"/> to return atlas slices.
    /// </summary>
    public Texture(INativeTexture backendTexture, RectangleF uvRect)
    {
        BackendTexture = backendTexture;
        UvRect = uvRect;
        Width = (int)(backendTexture.Width * uvRect.Width);
        Height = (int)(backendTexture.Height * uvRect.Height);
    }

    /// <summary>
    /// Creates a dimension-only proxy texture with no GPU backing.
    /// Used by the video pipeline so <see cref="Drawable"/>
    /// can compute FillMode layout without knowing the underlying GPU resource type.
    /// </summary>
    public Texture(int width, int height)
    {
        BackendTexture = null;
        UvRect = new RectangleF(0, 0, 1, 1);
        Width = width;
        Height = height;
    }

    public void Dispose()
    {
        BackendTexture?.Dispose();
    }
}
