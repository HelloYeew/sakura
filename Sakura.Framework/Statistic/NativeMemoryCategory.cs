// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Statistic;

/// <summary>
/// What a block of unmanaged memory is being held for, as reported by
/// <see cref="NativeMemoryTracker"/>.
/// </summary>
public enum NativeMemoryCategory
{
    /// <summary>
    /// GPU textures created for sampling: images, atlas pages, glyph pages.
    /// </summary>
    Textures,

    /// <summary>
    /// GPU render targets — the color attachments behind <c>IFrameBuffer</c>.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Textures"/> even though the underlying object is the same kind of GPU
    /// texture, because their lifetimes are driven by completely different things: one by what content is
    /// loaded, the other by window size and how many buffered containers exist.
    /// </remarks>
    FrameBuffers,

    /// <summary>
    /// The per-plane GPU textures behind video playback.
    /// </summary>
    Video,

    /// <summary>
    /// Encoded audio held outside the managed heap for as long as the audio backend reads it.
    /// </summary>
    Audio,

    /// <summary>
    /// Unmanaged memory that does not fall into a category above.
    /// </summary>
    Other,
}
