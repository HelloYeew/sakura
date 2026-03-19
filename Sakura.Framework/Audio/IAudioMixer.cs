// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Audio;

/// <summary>
/// An audio channel that mix multiple <see cref="IAudioChannel"/>s together into a single output stream.
/// </summary>
public interface IAudioMixer : IAudioChannel
{
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
