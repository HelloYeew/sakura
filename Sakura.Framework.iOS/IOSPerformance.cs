// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Diagnostics;
using System.Threading;
using Foundation;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;

namespace Sakura.Framework.iOS;

/// <summary>
/// What iOS says of the device's heat and battery saving. It has no frame hints or game states to send: the system
/// puts a game in Game Mode itself, by its Info.plist (a games category and <c>LSSupportsGameMode</c>), unasked.
/// </summary>
/// <remarks>
/// The system is asked at most once a second. Each answer is a call into Objective-C through the bindings, and the host
/// reads both on every update, 120 times a second, for states that change over seconds rather than frames.
/// </remarks>
internal sealed class IOSPerformance : IPlatformPerformance
{
    private const long read_interval_ms = 1000;

    private static readonly long read_interval_ticks = Stopwatch.Frequency * read_interval_ms / 1000;

    /// <summary>
    /// The last answer, replaced only when one of its values changes.
    /// </summary>
    private Reading reading;

    /// <summary>
    /// When the system was last asked, in <see cref="Stopwatch"/> ticks.
    /// </summary>
    private long lastRead;

    /// <summary>
    /// Asks the system once, so the first frame has an answer, and logs it.
    /// </summary>
    public IOSPerformance()
    {
        var info = NSProcessInfo.ProcessInfo;

        reading = new Reading(info.ThermalState, info.LowPowerModeEnabled);
        lastRead = Stopwatch.GetTimestamp();

        Logger.Verbose($"Thermal state: {reading.ThermalState}");
        Logger.Verbose($"Low Power Mode is {(reading.PowerSaving ? "on" : "off")}");
    }

    public float ThermalHeadroom => current().ThermalHeadroom;

    public bool? PowerSaving => current().PowerSaving;

    /// <summary>
    /// The answer, asking the system again first if the last time was a second or more ago. Any thread may call this:
    /// two arriving together may both ask, which costs a call and changes nothing.
    /// </summary>
    private Reading current()
    {
        long now = Stopwatch.GetTimestamp();

        if (now - Volatile.Read(ref lastRead) >= read_interval_ticks)
        {
            Volatile.Write(ref lastRead, now);
            refresh();
        }

        return Volatile.Read(ref reading);
    }

    /// <summary>
    /// Asks the system, and keeps and logs the answer if it changed.
    /// </summary>
    private void refresh()
    {
        var info = NSProcessInfo.ProcessInfo;
        var thermalState = info.ThermalState;
        bool powerSaving = info.LowPowerModeEnabled;
        var last = Volatile.Read(ref reading);

        if (thermalState == last.ThermalState && powerSaving == last.PowerSaving)
            return;

        if (thermalState != last.ThermalState)
            Logger.Verbose($"Thermal state: {last.ThermalState} → {thermalState}");

        if (powerSaving != last.PowerSaving)
            Logger.Verbose($"Low Power Mode is {(powerSaving ? "on" : "off")}");

        Volatile.Write(ref reading, new Reading(thermalState, powerSaving));
    }

    /// <summary>
    /// One answer from the system. A class, so a new one is put in place whole and no thread sees half of it.
    /// </summary>
    private sealed class Reading
    {
        /// <summary>
        /// The system's step for the device's heat.
        /// </summary>
        public NSProcessInfoThermalState ThermalState { get; }

        /// <summary>
        /// Whether Low Power Mode is on.
        /// </summary>
        public bool PowerSaving { get; }

        /// <summary>
        /// <see cref="ThermalState"/> as <see cref="IPlatformPerformance.ThermalHeadroom"/> has it. iOS gives four steps
        /// rather than a forecast. Serious is where it starts slowing the device down, which is 1 here; fair is halfway
        /// there, critical past it.
        /// </summary>
        public float ThermalHeadroom { get; }

        /// <summary>
        /// Keeps an answer, with its headroom worked out once.
        /// </summary>
        public Reading(NSProcessInfoThermalState thermalState, bool powerSaving)
        {
            ThermalState = thermalState;
            PowerSaving = powerSaving;
            ThermalHeadroom = thermalState switch
            {
                NSProcessInfoThermalState.Nominal => 0,
                NSProcessInfoThermalState.Fair => 0.5f,
                NSProcessInfoThermalState.Serious => 1,
                _ => 1.5f
            };
        }
    }
}
