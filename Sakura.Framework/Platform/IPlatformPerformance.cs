// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Platform;

/// <summary>
/// Performance services the platform offers an app: telling the system how much CPU each frame needs, so it raises the
/// clocks before a frame runs late rather than after, and what the game is doing; and reading how close the device is
/// to slowing itself down for heat, whether battery saver is on, and the mode the player picked for the game. Android
/// has them all. Elsewhere every member does nothing and reports nothing.
/// </summary>
public interface IPlatformPerformance
{
    /// <summary>
    /// How close the device is to being slowed down for heat, forecast a few seconds ahead: 0 is cool, and at 1 the
    /// system starts throttling. An app can do less as it nears 1 (fewer effects, a lower resolution) so that doesn't
    /// happen. NaN where the platform doesn't say. Cheap enough to read every frame.
    /// </summary>
    float ThermalHeadroom => float.NaN;

    /// <summary>
    /// Whether the user has battery saver on, or null where the platform doesn't say. The system already slows the device
    /// down for it, with a lower display rate and slower CPUs; an app can do less to match, such as fewer effects. Cheap
    /// enough to read every frame.
    /// </summary>
    bool? PowerSaving => null;

    /// <summary>
    /// The mode the player picked for this game in the system's game settings (Android's Game Dashboard), for a game to
    /// set its own defaults by: the most frames for <see cref="Platform.GameMode.Performance"/>, fewer for
    /// <see cref="Platform.GameMode.Battery"/>. Null where the platform has no game modes, or where the app isn't a game
    /// to it (on Android, one whose manifest doesn't give it the game category). Cheap enough to read every frame.
    /// </summary>
    GameMode? GameMode => null;

    /// <summary>
    /// Tells the system what the game is doing, so it can plan for it: it raises the CPU for loading, and interrupts
    /// play that can't be paused less. Does nothing where the platform doesn't take it.
    /// </summary>
    /// <param name="state">What the player is doing.</param>
    /// <param name="loading">Whether the game is loading, whatever it shows meanwhile.</param>
    void SetGameState(GameState state, bool loading)
    {
    }

    /// <summary>
    /// Opens a session that tells the system how long each frame of the calling thread takes against its target.
    /// Called on that thread as it starts. Unless power efficiency is preferred, a platform may also raise the thread's
    /// priority, even where it has no sessions. Null where the platform has no such service.
    /// </summary>
    /// <param name="threadName">The thread's name, for logging.</param>
    /// <param name="targetMilliseconds">The first frame's target.</param>
    /// <param name="preferPowerEfficiency">
    /// Whether the system may favour the battery over speed, for an app that only has to run smoothly.
    /// </param>
    IFrameWorkHint? CreateFrameWorkHint(string threadName, double targetMilliseconds, bool preferPowerEfficiency) => null;
}

/// <summary>
/// One thread's session from <see cref="IPlatformPerformance.CreateFrameWorkHint"/>. Used and disposed on that thread.
/// </summary>
public interface IFrameWorkHint : IDisposable
{
    /// <summary>
    /// Reports a finished frame.
    /// </summary>
    /// <param name="targetMilliseconds">The frame's target, or 0 if it had none, in which case it isn't reported.</param>
    /// <param name="workMilliseconds">How long the frame's work took, not counting waits on a device.</param>
    void ReportFrame(double targetMilliseconds, double workMilliseconds);
}

/// <summary>
/// The services of a platform that has none.
/// </summary>
internal sealed class NoPlatformPerformance : IPlatformPerformance
{
}
