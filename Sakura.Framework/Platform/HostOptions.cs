// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Platform;

public class HostOptions
{
    /// <summary>
    /// THe friendly name of the application that will use to display in the title or as a display name.
    /// </summary>
    /// <remarks>
    /// If empty, the framework will use a default name based on the name in the app name.
    /// </remarks>
    public string FriendlyAppName { get; set; } = string.Empty;

    /// <summary>
    /// Whether this is a portable installation. Will cause all application files to be placed alongside the executable, rather than in the standard data directory.
    /// </summary>
    public bool PortableInstallation { get; set; }

    /// <summary>
    /// What kind of application this is. <see cref="Platform.AppType.Application"/> trades the precise timing a
    /// game needs for far less CPU, and makes <see cref="AllowThreadSpinning"/> and
    /// <see cref="UseMultimediaClassScheduler"/> have no effect.
    /// </summary>
    public AppType AppType { get; set; } = AppType.Game;

    /// <summary>
    /// Whether to limit the update thread FPS to maximum at 1000 FPS in order to prevent excessive CPU usage
    /// when the game is running with an unlimited update rate.
    /// </summary>
    public bool LimitUnlimitedUpdateRate { get; set; } = true;

    /// <summary>
    /// Whether the update and draw threads (and the main loop, when single-threaded) may busy-spin the
    /// last stretch of each frame to land exactly on their deadline. How long that stretch is depends
    /// on how late the platform's sleep wakes, from tens of microseconds on macOS to over half a
    /// millisecond on Windows. This gives the most precise frame timing on desktop, at the cost of
    /// constant CPU usage. Disable on battery-constrained targets (e.g. mobile) to trade
    /// sub-millisecond pacing jitter for power savings. Spinning is automatically suspended while the
    /// window is inactive regardless of this setting, and the audio thread and multi-threaded input
    /// loop never spin. Only applies to <see cref="Platform.AppType.Game"/>.
    /// </summary>
    /// <remarks>
    /// Off by default on phones and tablets, where a spinning core costs battery and heat, and a hot phone
    /// slows its clocks down and misses more frames than spinning saved.
    /// </remarks>
    public bool AllowThreadSpinning { get; set; } = !RuntimeInfo.IsMobile;

    /// <summary>
    /// Windows only. Whether to register the update and draw threads with the Multimedia Class
    /// Scheduler Service as a "Games" task, which lifts them above ordinary threads so one woken by its
    /// timer does not wait behind other work for a CPU. Only applies to <see cref="Platform.AppType.Game"/>.
    /// </summary>
    /// <remarks>
    /// Off by default until measured. The service reserves a share of CPU for everything else (20% by
    /// default) and drops registered threads to a low priority while they are over it, which these
    /// threads' busy-spinning may trigger.
    /// </remarks>
    public bool UseMultimediaClassScheduler { get; set; }
}
