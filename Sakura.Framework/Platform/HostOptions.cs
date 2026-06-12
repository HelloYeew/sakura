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
    /// Whether to limit the update thread FPS to maximum at 1000 FPS in order to prevent excessive CPU usage
    /// when the game is running with an unlimited update rate.
    /// </summary>
    public bool LimitUnlimitedUpdateRate { get; set; } = true;

    /// <summary>
    /// Whether frame-pacing threads may busy-spin for the last ~0.5ms of each frame to land
    /// exactly on their deadline. This gives the most precise frame timing on desktop, at the
    /// cost of constant CPU usage. Disable on battery-constrained targets (e.g. mobile) to
    /// trade sub-millisecond pacing jitter for power savings. Spinning is automatically
    /// suspended while the window is inactive regardless of this setting.
    /// </summary>
    public bool AllowThreadSpinning { get; set; } = true;
}
