// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;

namespace Sakura.Framework.Audio;

/// <summary>
/// An audio channel that mix multiple <see cref="IAudioChannel"/>s together into a single output stream.
/// </summary>
public interface IAudioMixer : IAudioChannel
{
    /// <summary>
    /// Gets the list of active channels currently routed into this mixer.
    /// </summary>
    IEnumerable<IAudioChannel> ActiveChannels { get; }

    /// <summary>
    /// Add an audio channel to this mixer.
    /// </summary>
    /// <param name="channel">The channel to add.</param>
    void AddChannel(IAudioChannel channel);

    /// <summary>
    /// Remove an audio channel from this mixer.
    /// </summary>
    /// <param name="channel">The channel to remove.</param>
    void RemoveChannel(IAudioChannel channel);
}
