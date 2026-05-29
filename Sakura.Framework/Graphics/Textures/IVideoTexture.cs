// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// Minimal view of a video texture used by <see cref="ITextureManager"/> for tracking
/// and by <see cref="Sakura.Framework.Graphics.Performance.TextureViewerDisplay"/> for preview.
/// Defined in the Textures namespace to avoid a circular dependency with the Video namespace.
/// </summary>
public interface IVideoTexture
{
    int Width { get; }
    int Height { get; }

    /// <summary>
    /// True once the GPU upload for the current frame is complete.
    /// </summary>
    bool UploadComplete { get; }

    /// <summary>
    /// The Y-plane GL texture handle, usable as a greyscale preview.
    /// </summary>
    uint YPlaneHandle { get; }
}
