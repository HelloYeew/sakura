// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Audio;

/// <summary>
/// An audio manager with an output nothing else stops while the app is in the background, so the host does.
/// </summary>
internal interface IStopsInBackground
{
    /// <summary>
    /// Stops the output. Called with the audio thread parked.
    /// </summary>
    void EnterBackground();

    /// <summary>
    /// Starts the output again. Called with the audio thread still parked.
    /// </summary>
    void EnterForeground();
}
