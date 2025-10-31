// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Audio;

/// <summary>
/// Represents a loaded audio sample (sound effect)
/// </summary>
public interface ISample
{
    /// <summary>
    /// Plays the sample and returns a dedicated channel
    /// </summary>
    /// <returns>A channel to control the playback</returns>
    IAudioChannel Play();

    /// <summary>
    /// Gets the length of the sample in milliseconds
    /// </summary>
    double Length { get; }
}
