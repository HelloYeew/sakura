// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Threading;

/// <summary>
/// Which of the host's threads a <see cref="ThreadActivity"/> is applied to, since each asks for something different at
/// the same level.
/// </summary>
internal enum ThreadRole
{
    /// <summary>
    /// The update thread, which runs the app's logic and input.
    /// </summary>
    Update,

    /// <summary>
    /// The draw thread, which sends each frame to the GPU.
    /// </summary>
    Draw,

    /// <summary>
    /// The audio thread, which advances the audio state. The sound itself is mixed on the device's own thread.
    /// </summary>
    Audio
}
