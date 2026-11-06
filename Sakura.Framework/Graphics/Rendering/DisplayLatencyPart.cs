// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Timing;

namespace Sakura.Framework.Graphics.Rendering;

/// <summary>
/// One part of <see cref="IRenderer.DisplayLatencyMilliseconds"/>, for explaining it: where the frame spends the time,
/// how long, and how that's known.
/// </summary>
/// <param name="Kind">Where the frame spends the time.</param>
/// <param name="Milliseconds">How long, or null where nothing can tell, which counts as 0 in the total.</param>
/// <param name="Source">How it's known: <see cref="LatencySource.NotReported"/> exactly when there's no figure.</param>
public readonly record struct DisplayLatencyPart(DisplayLatencyPartKind Kind, double? Milliseconds, LatencySource Source)
{
    /// <summary>
    /// A part that's there but that nothing can tell.
    /// </summary>
    public static DisplayLatencyPart NotReported(DisplayLatencyPartKind kind) => new DisplayLatencyPart(kind, null, LatencySource.NotReported);
}
