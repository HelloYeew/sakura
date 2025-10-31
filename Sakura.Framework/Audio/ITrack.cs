// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Audio;

/// <summary>
/// Represent a loaded audio track (music)
/// </summary>
public interface ITrack
{
    /// <summary>
    /// Plays the track and return a dedicated channel
    /// </summary>
    /// <returns>A channel to control the playback</returns>
    IAudioChannel Play();

    /// <summary>
    /// Gets the length of the track in milliseconds
    /// </summary>
    double Length { get; }

    /// <summary>
    /// Gets or sets the position in milliseconds to loop back to when <see cref="IAudioChannel.Looping"/> is enabled.
    /// </summary>
    double RestartPoint { get; set; }
}
