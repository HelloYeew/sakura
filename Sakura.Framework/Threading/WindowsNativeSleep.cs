// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Sakura.Framework.Threading;

/// <summary>
/// High-precision sleep on Windows using a high-resolution waitable timer by
/// use <c>CreateWaitableTimerEx</c> with <c>CREATE_WAITABLE_TIMER_HIGH_RESOLUTION</c>
/// (available since Windows 10 1803) which lets the kernel wake us at the exact requested
/// time without requiring any spin-wait or timer resolution hacks, will fall back
/// to a standard waitable timer if the high-resolution flag is unsupported,
/// and returns false (falling back to Thread.Sleep) if the timer cannot be created at all.
/// </summary>
[SupportedOSPlatform("windows")]
internal class WindowsNativeSleep : INativeSleep
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public int dwLowDateTime;
        public int dwHighDateTime;

        public static FileTime FromTimeSpan(TimeSpan ts)
        {
            ulong ul = unchecked((ulong)-ts.Ticks);
            return new FileTime
            {
                dwLowDateTime = (int)(ul & 0xFFFFFFFF),
                dwHighDateTime = (int)(ul >> 32),
            };
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateWaitableTimerEx(
        IntPtr lpTimerAttributes,
        string? lpTimerName,
        uint dwFlags,
        uint dwDesiredAccess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetWaitableTimerEx(
        IntPtr hTimer,
        in FileTime lpDueTime,
        int lPeriod,
        IntPtr pfnCompletionRoutine,
        IntPtr lpArgToCompletionRoutine,
        IntPtr wakeContext,
        uint tolerableDelay);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint create_waitable_timer_manual_reset = 0x00000001;
    private const uint create_waitable_timer_high_resolution = 0x00000002;
    private const uint timer_all_access = 2031619U;
    private const uint infinite = 0xFFFFFFFF;

    private readonly IntPtr waitableTimer;

    public WindowsNativeSleep()
    {
        // Try high-resolution timer first (Windows 10 1803+).
        waitableTimer = CreateWaitableTimerEx(
            IntPtr.Zero, null,
            create_waitable_timer_manual_reset | create_waitable_timer_high_resolution,
            timer_all_access);

        if (waitableTimer == IntPtr.Zero)
        {
            // Fall back to standard waitable timer — still more accurate than Thread.Sleep.
            waitableTimer = CreateWaitableTimerEx(
                IntPtr.Zero, null,
                create_waitable_timer_manual_reset,
                timer_all_access);
        }
    }

    public bool Sleep(TimeSpan duration)
    {
        if (waitableTimer == IntPtr.Zero)
            return false;

        var dueTime = FileTime.FromTimeSpan(duration);

        if (!SetWaitableTimerEx(waitableTimer, in dueTime, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0))
            return false;

        _ = WaitForSingleObject(waitableTimer, infinite);
        return true;
    }

    public void Dispose()
    {
        if (waitableTimer != IntPtr.Zero)
            _ = CloseHandle(waitableTimer);
    }
}
