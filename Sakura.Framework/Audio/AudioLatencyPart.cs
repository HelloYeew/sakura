// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Timing;

namespace Sakura.Framework.Audio;

/// <summary>
/// One part of <see cref="IAudioManager.OutputLatencyMs"/> for explanation where the sound waits, for how long, and how that's known.
/// </summary>
/// <param name="Kind">Where the sound waits.</param>
/// <param name="Milliseconds">How long, or null where nothing reports it, which counts as 0 in the total.</param>
/// <param name="Source">How it's known, <see cref="LatencySource.NotReported"/> exactly when there's no figure.</param>
public readonly record struct AudioLatencyPart(AudioLatencyPartKind Kind, double? Milliseconds, LatencySource Source)
{
    /// <summary>
    /// A part that's there but that nothing reports.
    /// </summary>
    public static AudioLatencyPart NotReported(AudioLatencyPartKind kind) => new AudioLatencyPart(kind, null, LatencySource.NotReported);
}
