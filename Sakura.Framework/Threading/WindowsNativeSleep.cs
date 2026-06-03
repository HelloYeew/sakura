// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Sakura.Framework.Threading;

/// <summary>
/// High-precision sleep on Windows using NtDelayExecution via ntdll.
/// Requests a 1 ms timer resolution for the lifetime of the instance.
/// </summary>
[SupportedOSPlatform("windows")]
internal class WindowsNativeSleep : INativeSleep
{
    // Sets the system timer resolution (100-ns units).
    [DllImport("ntdll.dll", SetLastError = false)]
    private static extern int NtSetTimerResolution(uint desiredResolution, bool setResolution, out uint currentResolution);

    // Alertable, relative sleep in 100-ns units (negative = relative).
    [DllImport("ntdll.dll", SetLastError = false)]
    private static extern int NtDelayExecution(bool alertable, ref long delayInterval);

    // 1 ms in 100-ns units.
    private const uint resolution1_ms = 10_000;

    public WindowsNativeSleep()
    {
        _ = NtSetTimerResolution(resolution1_ms, true, out _);
    }

    public bool Sleep(TimeSpan duration)
    {
        // NtDelayExecution uses negative 100-ns intervals for relative delays.
        long interval = -(long)(duration.TotalNanoseconds / 100);
        _ = NtDelayExecution(false, ref interval);
        return true;
    }

    public void Dispose()
    {
        _ = NtSetTimerResolution(resolution1_ms, false, out _);
    }
}
