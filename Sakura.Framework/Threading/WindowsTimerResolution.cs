// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Globalization;
using Sakura.Framework.Logging;
using static SDL.SDL3;

namespace Sakura.Framework.Threading;

/// <summary>
/// The Windows timer resolution the process holds at each <see cref="ThreadActivity"/>. SDL raises it to 1 ms for the
/// whole process when it starts, which makes the system wake more often for every app, and costs power. The frame
/// threads wait on high-resolution timers that don't need it, but sleeps elsewhere do, so it's only handed back while
/// the app is out of the user's way.
/// </summary>
internal static class WindowsTimerResolution
{
    /// <summary>
    /// The resolution, in milliseconds, at a level. 0 hands it back to the system.
    /// </summary>
    internal static int PeriodFor(ThreadActivity activity) => activity is ThreadActivity.Background or ThreadActivity.Hidden ? 0 : 1;

    /// <summary>
    /// Asks SDL for the resolution of a level. Safe from any thread.
    /// </summary>
    public static void Apply(ThreadActivity activity)
    {
        int period = PeriodFor(activity);

        if (SDL_SetHint(SDL_HINT_TIMER_RESOLUTION, period.ToString(CultureInfo.InvariantCulture)))
            Logger.Debug(period == 0 ? "Timer resolution handed back to the system" : $"Timer resolution held at {period} ms");
    }
}
