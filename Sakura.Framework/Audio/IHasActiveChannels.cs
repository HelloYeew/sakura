// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Audio;

/// <summary>
/// Implemented by an audio component that knows whether anything is still playing it.
/// </summary>
/// <remarks>
/// <see cref="AudioStore{T}"/> consults this before evicting a least-recently-used entry: a track
/// that has not been touched in a while may still be the one currently playing, and disposing it
/// would free the decoder state underneath a live channel. Components that do not implement this
/// are treated as always evictable, which is the correct answer for anything holding no state a
/// channel can outlive.
/// </remarks>
public interface IHasActiveChannels
{
    /// <summary>
    /// Whether at least one channel created from this component is still alive.
    /// </summary>
    bool HasActiveChannels { get; }
}
