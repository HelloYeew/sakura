// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.InteropServices;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Android;

/// <summary>
/// Android's performance services: hint sessions (ADPF) for the frame threads and the thermal headroom, both from the
/// NDK. A hint session tells the system how long each frame's work took against its target, so it raises the clocks
/// before frames run late instead of after; without one, a thread that has to finish in time could only spin.
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
        // The NDK API came in 33. Android 12 has it in Java only, and few devices backed it before 13.
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
            return null;

        IntPtr manager = APerformanceHint_getManager();

        if (manager == IntPtr.Zero)
            return null;

        int threadId = gettid();
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
