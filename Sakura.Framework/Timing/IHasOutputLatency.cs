// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Timing;

/// <summary>
/// A clock whose time is what's heard of the audio it plays, such as a <see cref="Audio.TrackClock"/>: started from a
/// position, it's only heard there once the output has carried the audio that far.
/// </summary>
public interface IHasOutputLatency
{
    /// <summary>
    /// How long after starting from a position the clock gets there, in milliseconds of real time.
    /// </summary>
    double OutputLatency { get; }
}
