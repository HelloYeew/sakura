// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.InteropServices;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Android;

/// <summary>
/// Android's performance services: hint sessions (ADPF) for the frame threads and, for a game, a higher priority for
/// them, and the thermal headroom; from the NDK and libc. A hint session tells the system how long each frame's work
/// took against its target, so it raises the clocks before frames run late instead of after; without one, a thread
/// that has to finish in time could only spin.
/// </summary>
internal sealed class AndroidPerformance : IPlatformPerformance
{
    private const string library = "android";

    // How far ahead the thermal headroom is forecast: far enough to do less before throttling, near enough to be right.
    private const int thermal_forecast_seconds = 10;

    // Android answers with NaN when asked more often than about once a second.
    private const long thermal_poll_interval_ms = 1000;

    private readonly IntPtr thermalManager;
    private readonly object thermalLock = new object();
    private float thermalHeadroom = float.NaN;
    private long lastThermalPoll = long.MinValue;

    public AndroidPerformance()
    {
        // The headroom came in API 31; the manager in 30.
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
            thermalManager = AThermal_acquireManager();
    }

    public float ThermalHeadroom
    {
        get
        {
            if (thermalManager == IntPtr.Zero)
                return float.NaN;

            long now = Environment.TickCount64;

            lock (thermalLock)
            {
                if (now - lastThermalPoll < thermal_poll_interval_ms)
                    return thermalHeadroom;

                lastThermalPoll = now;

                float headroom = AThermal_getThermalHeadroom(thermalManager, thermal_forecast_seconds);

                // A NaN from asking too soon keeps the last answer; one from the start means the device doesn't say.
                if (!float.IsNaN(headroom))
                    thermalHeadroom = headroom;

                return thermalHeadroom;
            }
        }
    }

    public IFrameWorkHint? CreateFrameWorkHint(string threadName, double targetMilliseconds, bool preferPowerEfficiency)
    {
        int threadId = gettid();

        if (!preferPowerEfficiency)
            raiseToUrgentDisplayPriority(threadName, threadId);

        // The NDK API came in 33. Android 12 has it in Java only, and few devices backed it before 13.
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
            return null;

        IntPtr manager = APerformanceHint_getManager();

        if (manager == IntPtr.Zero)
            return null;

        IntPtr session = APerformanceHint_createSession(manager, ref threadId, 1, nanoseconds(targetMilliseconds));

        if (session == IntPtr.Zero)
        {
            Logger.Verbose($"No performance hint session for {threadName}: the device doesn't offer them.");
            return null;
        }

        if (preferPowerEfficiency && OperatingSystem.IsAndroidVersionAtLeast(35))
            APerformanceHint_setPreferPowerEfficiency(session, true);

        Logger.Verbose($"Performance hint session for {threadName} (thread {threadId}): {targetMilliseconds:0.##} ms a frame"
                       + (preferPowerEfficiency ? ", preferring power efficiency" : string.Empty));

        return new FrameWorkHint(session, targetMilliseconds);
    }

    // Android's THREAD_PRIORITY_URGENT_DISPLAY. Every thread the app makes already runs at THREAD_PRIORITY_DISPLAY (-4),
    // which SDL gives its own thread and every thread made from it inherits, the app's background work (decoders,
    // loading) included. This puts a game's frame threads ahead of that work, still behind audio (-16).
    private const int urgent_display_priority = -8;

    // sched_setscheduler's SCHED_OTHER, the normal policy, with SCHED_RESET_ON_FORK: a thread made by one with a raised
    // priority starts at the default instead of inheriting it, so thread-pool workers and decoders started from a
    // frame thread don't end up level with it.
    private const int sched_other_reset_on_fork = 0x40000000;

    // setpriority's PRIO_PROCESS, which on Linux, given a thread's ID, applies to that thread alone.
    private const int prio_process = 0;

    private static void raiseToUrgentDisplayPriority(string threadName, int threadId)
    {
        var parameters = new SchedParam();

        if (sched_setscheduler(threadId, sched_other_reset_on_fork, ref parameters) != 0)
        {
            Logger.Verbose($"{threadName} stays at its priority: threads it made would inherit a raised one (error {Marshal.GetLastPInvokeError()}).");
            return;
        }

        if (setpriority(prio_process, threadId, urgent_display_priority) != 0)
            Logger.Verbose($"{threadName} stays at its priority: setpriority failed (error {Marshal.GetLastPInvokeError()}).");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SchedParam
    {
        public int Priority;
    }

    private static long nanoseconds(double milliseconds) => (long)(milliseconds * 1_000_000);

    private sealed class FrameWorkHint : IFrameWorkHint
    {
        private IntPtr session;
        private long targetNanoseconds;

        public FrameWorkHint(IntPtr session, double targetMilliseconds)
        {
            this.session = session;
            targetNanoseconds = nanoseconds(targetMilliseconds);
        }

        public void ReportFrame(double targetMilliseconds, double workMilliseconds)
        {
            long target = nanoseconds(targetMilliseconds);
            long work = nanoseconds(workMilliseconds);

            // Both must be positive; a frame with no target has nothing to be measured against.
            if (session == IntPtr.Zero || target <= 0 || work <= 0)
                return;

            if (target != targetNanoseconds)
            {
                targetNanoseconds = target;
                APerformanceHint_updateTargetWorkDuration(session, target);
            }

            APerformanceHint_reportActualWorkDuration(session, work);
        }

        public void Dispose()
        {
            if (session == IntPtr.Zero)
                return;

            APerformanceHint_closeSession(session);
            session = IntPtr.Zero;
        }
    }

    [DllImport("c")]
    private static extern int gettid();

    [DllImport("c", SetLastError = true)]
    private static extern int sched_setscheduler(int pid, int policy, ref SchedParam parameters);

    [DllImport("c", SetLastError = true)]
    private static extern int setpriority(int which, int who, int priority);

    [DllImport(library)]
    private static extern IntPtr APerformanceHint_getManager();

    [DllImport(library)]
    private static extern IntPtr APerformanceHint_createSession(IntPtr manager, ref int threadIds, nuint size, long initialTargetWorkDurationNanos);

    [DllImport(library)]
    private static extern int APerformanceHint_updateTargetWorkDuration(IntPtr session, long targetDurationNanos);

    [DllImport(library)]
    private static extern int APerformanceHint_reportActualWorkDuration(IntPtr session, long actualDurationNanos);

    [DllImport(library)]
    private static extern int APerformanceHint_setPreferPowerEfficiency(IntPtr session, [MarshalAs(UnmanagedType.U1)] bool enabled);

    [DllImport(library)]
    private static extern void APerformanceHint_closeSession(IntPtr session);

    [DllImport(library)]
    private static extern IntPtr AThermal_acquireManager();

    [DllImport(library)]
    private static extern float AThermal_getThermalHeadroom(IntPtr manager, int forecastSeconds);
}
