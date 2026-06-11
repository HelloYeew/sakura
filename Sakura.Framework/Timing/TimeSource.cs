// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Diagnostics;

namespace Sakura.Framework.Timing;

/// <summary>
/// The process-wide monotonic time source that all framework clocks derive from.
/// Every clock in the framework reports time on this single shared timeline (in milliseconds
/// since process start). This makes timestamps taken on different threads — input, audio,
/// update, draw — directly comparable, which is the foundation for things like
/// accurate hit judgement in rhythm games: <c>inputTime - audioTime</c>
/// is only meaningful when both sides share an epoch.
/// </summary>
public static class TimeSource
{
    private static readonly long epoch = Stopwatch.GetTimestamp();
    private static readonly double ms_per_tick = 1000.0 / Stopwatch.Frequency;

    /// <summary>
    /// The current time in milliseconds on the shared timeline.
    /// Monotonic, unaffected by system clock changes, safe to read from any thread.
    /// </summary>
    public static double CurrentTime => (Stopwatch.GetTimestamp() - epoch) * ms_per_tick;

    /// <summary>
    /// Converts a raw <see cref="Stopwatch.GetTimestamp"/> value onto the shared timeline.
    /// </summary>
    /// <param name="stopwatchTimestamp">A timestamp obtained from <see cref="Stopwatch.GetTimestamp"/>.</param>
    /// <returns>The equivalent time in milliseconds on the shared timeline.</returns>
    public static double FromStopwatchTimestamp(long stopwatchTimestamp) => (stopwatchTimestamp - epoch) * ms_per_tick;
}
