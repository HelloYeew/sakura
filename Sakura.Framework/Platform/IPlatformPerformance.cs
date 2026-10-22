// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Platform;

/// <summary>
/// Performance services the platform offers an app: telling the system how much CPU each frame needs, so it raises the
/// clocks before a frame runs late rather than after, and how close the device is to slowing itself down for heat.
/// Android has both. Elsewhere every member does nothing and reports nothing.
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
