// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Audio;

/// <summary>
/// Interface for a store that retrieves audio components.
/// </summary>
/// <typeparam name="T">The type of audio component (ITrack or ISample).</typeparam>
public interface IAudioStore<T> where T : class
{
    /// <summary>
    /// Retrieves an audio component by its name (path).
    /// </summary>
    /// <param name="name">The name/path of the resource (e.g. "Audio/Music/track1.mp3").</param>
    /// <returns>The loaded audio component, or null if not found.</returns>
    T Get(string name);
}
