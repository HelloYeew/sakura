// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Timing;

/// <summary>
/// A clock that derives its time from another <see cref="IClock"/> and allows
/// that source to be exchanged at runtime.
/// </summary>
public interface ISourceChangeableClock : IClock
{
    /// <summary>
    /// The source this clock derives its time from.
    /// </summary>
    IClock Source { get; }

    /// <summary>
    /// Exchanges the source of this clock.
    /// </summary>
    /// <param name="source">The new source.</param>
    void ChangeSource(IClock source);
}
