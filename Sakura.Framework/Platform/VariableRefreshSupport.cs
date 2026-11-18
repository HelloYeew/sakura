// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Platform;

/// <summary>
/// Whether the display under a window would vary its refresh to meet each frame (G-SYNC, FreeSync, Adaptive-Sync,
/// ProMotion), as far as the platform can tell.
/// </summary>
public enum VariableRefreshSupport
{
    /// <summary>
    /// The platform can't tell, so only the player knows. Windows and Linux have no simple public way to ask, and there the
    /// driver and the compositor decide.
    /// </summary>
    Unknown,

    /// <summary>
    /// It wouldn't: the display's rate is fixed, the system composes the window at the display's rate (a macOS window that
    /// isn't fullscreen), or the platform paces the window its own way (a phone).
    /// </summary>
    Unavailable,

    /// <summary>
    /// It would. macOS says so for a fullscreen window on a screen whose shortest and longest refresh intervals differ.
    /// </summary>
    Available,
}
