// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Threading;

/// <summary>
/// What a platform does to one of the host's threads at each <see cref="ThreadActivity"/>: its priority, and on some
/// platforms a work interval that tells the system when each frame is due. Several of these can only be set by the
/// thread itself, so it's created, used and disposed on the thread it serves.
/// </summary>
internal interface IThreadPolicy : IDisposable
{
    /// <summary>
    /// Applies a level to the calling thread. Called before the first frame, then between frames whenever the level
    /// changes.
    /// </summary>
    /// <returns>What it set, such as "priority 47", for the log and the statistics.</returns>
    string Apply(ThreadActivity activity);

    /// <summary>
    /// Called just before each frame's work.
    /// </summary>
    /// <param name="frameMilliseconds">The thread's frame period, or 0 if it runs unlimited.</param>
    void BeginFrame(double frameMilliseconds);

    /// <summary>
    /// Called just after each frame's work.
    /// </summary>
    void EndFrame();
}
