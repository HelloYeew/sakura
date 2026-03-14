// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Audio;

/// <summary>
/// Represents a loaded audio sample (sound effect)
/// </summary>
public interface ISample
{
    /// <summary>
    /// Create a new channel to play this sample.
    /// </summary>
    /// <returns>A channel to control the playback</returns>
    IAudioChannel GetChannel();

    /// <summary>
    /// Gets the length of the sample in milliseconds
    /// </summary>
    double Length { get; }
}
