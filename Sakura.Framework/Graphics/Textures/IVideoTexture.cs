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
    /// True once this texture has been disposed. Its GPU planes are gone (or queued to go) once this
    /// is set, so nothing may bind them. The preview in
    /// <see cref="Sakura.Framework.Graphics.Performance.TextureViewerDisplay"/> can outlive a pool by
    /// up to one refresh, and checks this before drawing.
    /// </summary>
    bool IsDisposed { get; }

    /// <summary>
    /// The YUV -> RGB conversion matrix (3x3) matching the colorspace of the frames uploaded into this
    /// texture, or <see langword="null"/> if no frame has been uploaded yet. Stamped on by the decoder
    /// as it hands each frame over, so anything holding only the texture can convert it without
    /// reaching back into the decoder.
    /// </summary>
    float[]? ConversionMatrix { get; }

    /// <summary>
    /// Binds the Y, U, and V planes to the texture slots the video shader samples.
    /// Must be called on the render thread. See <see cref="INativeVideoTexture.BindPlanes"/> for
    /// <paramref name="tiling"/>.
    /// </summary>
    void BindPlanes(bool tiling);
}
