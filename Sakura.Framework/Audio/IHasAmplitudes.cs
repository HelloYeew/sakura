// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Audio;

/// <summary>
/// Implemented by audio sources that can report their current amplitude/frequency data.
/// Used by visualisers to read a live frequency spectrum from a playing channel.
/// </summary>
public interface IHasAmplitudes
{
    /// <summary>
    /// The most recent amplitude snapshot for this source.
    /// </summary>
    ChannelAmplitudes CurrentAmplitudes { get; }
}
