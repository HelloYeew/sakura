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
    /// Which plane set this texture holds. Selects the shader variant that can sample it —
    /// see <see cref="INativeVideoTexture.Layout"/>.
    /// </summary>
    VideoPlaneLayout Layout { get; }

    /// <summary>
    /// How often this texture's planes have been bound, per frame.
    /// See <see cref="INativeVideoTexture.Binds"/>.
    /// </summary>
    TextureBindCounter Binds { get; }

    /// <summary>
    /// True once the GPU upload for the current frame is complete.
    /// </summary>
    bool UploadComplete { get; }

    /// <summary>
    /// GPU bytes this texture's planes occupy, or 0 when it borrows them.
    /// See <see cref="INativeVideoTexture.PlaneBytes"/>.
    /// </summary>
    long PlaneBytes { get; }

    /// <summary>
    /// True once this texture has been disposed. Its GPU planes are gone (or queued to go) once this
    /// is set, so nothing may bind them. The preview in
    /// <see cref="Sakura.Framework.Graphics.Performance.TextureViewerDisplay"/> can outlive a pool by
    /// up to one refresh, and checks this before drawing.
    /// </summary>
    bool IsDisposed { get; }

    /// <summary>
    /// The affine YUV -> RGB transform matching the colorspace <em>and colour range</em> of the frames
    /// uploaded into this texture, as a column-major 4x4 float[16], or <see langword="null"/> if no
    /// frame has been uploaded yet. Stamped on by the decoder as it hands each frame over, so anything
    /// holding only the texture can convert it without reaching back into the decoder.
    /// </summary>
    float[]? ConversionMatrix { get; }

    /// <summary>
    /// Binds this texture's planes to the texture slots the video shader samples.
    /// Must be called on the render thread. See <see cref="INativeVideoTexture.BindPlanes"/> for
    /// <paramref name="tiling"/>.
    /// </summary>
    void BindPlanes(bool tiling);
}
