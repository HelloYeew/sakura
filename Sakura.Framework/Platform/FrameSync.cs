// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Platform;

/// <summary>
/// Defines how the application's update and draw loops are synchronized.
/// </summary>
public enum FrameSync
{
    /// <summary>
    /// Synchronize with the display's vertical refresh rate. Draw and update loops are locked to the display's speed.
    /// </summary>
    VSync,

    /// <summary>
    /// For a variable refresh rate display (G-SYNC, FreeSync, Adaptive-Sync, ProMotion): draw just under the display's top
    /// rate and present without waiting for a refresh, so the display starts one as each frame arrives. That's the latency
    /// of presenting without VSync, with no tearing and no frames queued. On a display whose rate is fixed it tears.
    /// Where the platform can tell the display won't vary its refresh for the window it follows <see cref="VSync"/>: on
    /// macOS anywhere but a fullscreen window on a screen whose rate varies, and always on a phone.
    /// </summary>
    VariableRefresh,

    /// <summary>
    /// Run the update loop at 2x the display's refresh rate. Drawing remains at display speed.
    /// </summary>
    Limit2x,

    /// <summary>
    /// Run the update loop at 4x the display's refresh rate. Drawing remains at display speed.
    /// </summary>
    Limit4x,

    /// <summary>
    /// Run the update loop at 8x the display's refresh rate. Drawing remains at display speed.
    /// </summary>
    Limit8x,

    /// <summary>
    /// Run the update and draw loops as fast as possible.
    /// </summary>
    Unlimited,
}
