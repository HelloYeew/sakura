// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Graphics.Rendering;

/// <summary>
/// What a renderer turns to when what carries the low latency mode stops working, so the mode keeps doing what it can with
/// nothing for the player to notice or set.
/// </summary>
internal static class LowLatencyFallback
{
    /// <summary>
    /// How many waits in a row may run out their timeout before the renderer stops waiting at all. A driver or GPU that never
    /// signals would otherwise hold every frame for the whole timeout.
    /// </summary>
    public const int TIMEOUTS_BEFORE_GIVING_UP = 3;

    /// <summary>
    /// What takes over from <paramref name="failed"/>: the driver's own (Reflex, Anti-Lag 2) gives way to waiting for the GPU,
    /// which works on any card, and that gives way to not waiting.
    /// </summary>
    public static LowLatencyProvider After(LowLatencyProvider failed) => failed is LowLatencyProvider.NvidiaReflex or LowLatencyProvider.AmdAntiLag ? LowLatencyProvider.GpuWait : LowLatencyProvider.None;

    /// <summary>
    /// <paramref name="provider"/>'s name, for the log.
    /// </summary>
    public static string Describe(LowLatencyProvider provider) => provider switch
    {
        LowLatencyProvider.NvidiaReflex => "NVIDIA Reflex",
        LowLatencyProvider.AmdAntiLag => "AMD Anti-Lag 2",
        LowLatencyProvider.GpuWait => "the wait for the GPU",
        _ => "nothing"
    };
}
