// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Audio;

/// <summary>
/// Represent a loaded audio track (music)
/// </summary>
public interface ITrack
{
    /// <summary>
    /// Creates a new channel to play this track.
    /// </summary>
    /// <returns>A channel to control the playback</returns>
    IAudioChannel GetChannel();

    /// <summary>
    /// Gets the length of the track in milliseconds
    /// </summary>
    double Length { get; }
}
