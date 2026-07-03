// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Threading;

/// <summary>
/// High-precision sleep on Windows.
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

    // ntdll timer-resolution APIs. Values are in 100ns units. These let us request the
    // finest resolution the platform supports (typically 5000 = 0.5ms), which is finer than
    // winmm's timeBeginPeriod (limited to whole milliseconds). status == 0 (STATUS_SUCCESS).
    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtQueryTimerResolution(out uint minimumResolution, out uint maximumResolution, out uint currentResolution);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtSetTimerResolution(uint desiredResolution, [MarshalAs(UnmanagedType.U1)] bool setResolution, out uint currentResolution);

    // winmm fallback used only if ntdll is unavailable for some reason.
    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint timeBeginPeriod(uint uMilliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint timeEndPeriod(uint uMilliseconds);

    private const uint create_waitable_timer_high_resolution = 0x00000002;
    private const uint timer_all_access = 2031619U;
    private const uint infinite = 0xFFFFFFFF;
    private const uint wait_object_0 = 0x00000000;
    private const int status_success = 0;

    /// <summary>
    /// Fallback resolution requested from winmm, in whole milliseconds.
    /// </summary>
    private const uint fallback_period_ms = 1;

    private readonly IntPtr waitableTimer;

    /// <summary>
    /// Whether the timer was created with the high-resolution flag. When false we are using a
    /// standard waitable timer, whose accuracy is bounded by the current system timer resolution
    /// (which is why we also raise it below).
    /// </summary>
    public bool IsHighResolution { get; }

    // Process-wide reference counting for the timer-resolution bump. Every AppThread creates its own
    // WindowsNativeSleep, so we must only raise the resolution once and restore it exactly once.
    private static int resolutionRefCount;
    private static uint appliedNtResolution; // 0 when ntdll path not in use
    private static bool appliedWinmmPeriod;

    public WindowsNativeSleep()
    {
        // Try high-resolution timer first (Windows 10 1803+). Auto-reset (no manual-reset flag).
        waitableTimer = CreateWaitableTimerEx(
            IntPtr.Zero, null,
            create_waitable_timer_high_resolution,
            timer_all_access);

        if (waitableTimer != IntPtr.Zero)
        {
            IsHighResolution = true;
        }
        else
        {
            // Fall back to standard auto-reset waitable timer — still more accurate than Thread.Sleep,
            // especially once we have raised the system timer resolution below.
            waitableTimer = CreateWaitableTimerEx(
                IntPtr.Zero, null,
                0,
                timer_all_access);
            IsHighResolution = false;
        }

        acquireTimerResolution();

        Logger.Debug($"WindowsNativeSleep initialized (highResolution: {IsHighResolution}, timerResolution: {describeResolution()})");
    }

    public bool Sleep(TimeSpan duration)
    {
        if (waitableTimer == IntPtr.Zero)
            return false;

        if (duration <= TimeSpan.Zero)
            return true;

        // Negative relative due time (100ns units). SetWaitableTimerEx interprets a negative
        // FILETIME as "this many 100ns intervals from now".
        var dueTime = FileTime.FromTimeSpan(duration);

        if (!SetWaitableTimerEx(waitableTimer, in dueTime, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0))
            return false;

        // Only treat the call as a successful precise sleep when the timer actually signaled.
        // If the wait fails for any reason, report false so the caller falls back to Thread.Sleep.
        return WaitForSingleObject(waitableTimer, infinite) == wait_object_0;
    }

    public void Dispose()
    {
        if (waitableTimer != IntPtr.Zero)
            _ = CloseHandle(waitableTimer);

        releaseTimerResolution();
    }

    /// <summary>
    /// Raises the process-wide timer resolution to the finest supported value. Reference counted so
    /// that multiple instances (one per thread) only apply the change once.
    /// </summary>
    private static void acquireTimerResolution()
    {
        if (Interlocked.Increment(ref resolutionRefCount) != 1)
            return;

        // Prefer ntdll: it can request finer-than-1ms resolution and reports the value actually applied.
        try
        {
            if (NtQueryTimerResolution(out _, out uint maximumResolution, out _) == status_success
                && NtSetTimerResolution(maximumResolution, true, out uint applied) == status_success)
            {
                appliedNtResolution = applied;
                return;
            }
        }
        catch (DllNotFoundException)
        {
            // ntdll missing (extremely unlikely on Windows) — fall through to winmm.
        }
        catch (EntryPointNotFoundException)
        {
            // fall through to winmm.
        }

        // Fallback: winmm's whole-millisecond period.
        if (timeBeginPeriod(fallback_period_ms) == 0 /* TIMERR_NOERROR */)
            appliedWinmmPeriod = true;
    }

    /// <summary>
    /// Restores the timer resolution once the last live instance is disposed.
    /// </summary>
    private static void releaseTimerResolution()
    {
        if (Interlocked.Decrement(ref resolutionRefCount) != 0)
            return;

        if (appliedNtResolution != 0)
        {
            _ = NtSetTimerResolution(appliedNtResolution, false, out _);
            appliedNtResolution = 0;
        }

        if (appliedWinmmPeriod)
        {
            _ = timeEndPeriod(fallback_period_ms);
            appliedWinmmPeriod = false;
        }
    }

    private static string describeResolution()
    {
        if (appliedNtResolution != 0)
            return $"{appliedNtResolution / 10000.0:0.###}ms (ntdll)";
        if (appliedWinmmPeriod)
            return $"{fallback_period_ms}ms (winmm)";

        return "system default";
    }
}
