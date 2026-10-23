// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Foundation;
using Sakura.Framework.Platform;

namespace Sakura.Framework.iOS;

/// <summary>
/// What iOS says of the device's heat and battery saving. It has no frame hints or game states to send: the system
/// puts a game in Game Mode itself, by its Info.plist (a games category and <c>LSSupportsGameMode</c>), unasked.
/// </summary>
internal sealed class IOSPerformance : IPlatformPerformance
{
    /// <summary>
    /// iOS gives four steps rather than a forecast. Serious is where it starts slowing the device down, which is 1 here;
    /// fair is halfway there, critical past it.
    /// </summary>
    public float ThermalHeadroom => NSProcessInfo.ProcessInfo.ThermalState switch
    {
        NSProcessInfoThermalState.Nominal => 0,
        NSProcessInfoThermalState.Fair => 0.5f,
        NSProcessInfoThermalState.Serious => 1,
        _ => 1.5f
    };

    public bool? PowerSaving => NSProcessInfo.ProcessInfo.LowPowerModeEnabled;
}
