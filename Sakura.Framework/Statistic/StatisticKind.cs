// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Statistic;

/// <summary>
/// What a statistic's number means over time
/// </summary>
public enum StatisticKind
{
    /// <summary>
    /// A reading of how things are right now like a queue depth, a heap size, a version string (Default)
    /// </summary>
    Gauge,

    /// <summary>
    /// Accumulates for the life of the process and is never reset
    /// </summary>
    Cumulative,

    /// <summary>
    /// Zeroed and re-accumulated every frame
    /// </summary>
    PerFrame
}
