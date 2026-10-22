// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Android.App;
using Android.Content;
using Android.OS;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;
using AndroidGameMode = Android.App.GameMode;
using AndroidGameState = Android.App.GameState;
using GameMode = Sakura.Framework.Platform.GameMode;
using GameState = Sakura.Framework.Platform.GameState;

namespace Sakura.Framework.Android;

/// <summary>
/// Android's performance services: hint sessions (ADPF) for the frame threads and, for a game, a higher priority for
/// them, from the NDK and libc; the thermal headroom from the NDK; battery saver, the game mode and the game state from
/// the system's managers. A hint session tells the system how long each frame's work took against its target, so it
/// raises the clocks before frames run late instead of after; without one, a thread that has to finish in time could
/// only spin.
/// </summary>
internal sealed class AndroidPerformance : IPlatformPerformance
{
    private const string library = "android";

    // How far ahead the thermal headroom is forecast: far enough to do less before throttling, near enough to be right.
    private const int thermal_forecast_seconds = 10;

    private readonly IntPtr thermalManager;
    private readonly GameManager? gameManager;

    private readonly Polled<float> thermalHeadroom;
    private readonly Polled<bool?> powerSaving;
    private readonly Polled<GameMode?> gameMode;

    public AndroidPerformance(Context context)
    {
        // The headroom came in API 31; the manager in 30.
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            thermalManager = AThermal_acquireManager();
            gameManager = context.GetSystemService(Context.GameService) as GameManager;
        }

        var powerManager = context.GetSystemService(Context.PowerService) as PowerManager;

        thermalHeadroom = new Polled<float>(float.NaN, last =>
        {
            if (thermalManager == IntPtr.Zero)
                return float.NaN;

            float headroom = AThermal_getThermalHeadroom(thermalManager, thermal_forecast_seconds);

            // A NaN from asking too soon keeps the last answer; one from the start means the device doesn't say.
            return float.IsNaN(headroom) ? last : headroom;
        });

        powerSaving = new Polled<bool?>(null, last =>
        {
            bool? saving = powerManager?.IsPowerSaveMode;

            if (saving != last)
                Logger.Verbose($"Battery saver is {(saving == true ? "on" : "off")}");

            return saving;
        });

        gameMode = new Polled<GameMode?>(null, last =>
        {
            var mode = OperatingSystem.IsAndroidVersionAtLeast(31) && gameManager != null ? gameModeOf((AndroidGameMode)gameManager.GameMode) : null;

            if (mode != last)
                Logger.Verbose($"Game mode: {mode?.ToString() ?? "none (not a game to the system)"}");

            return mode;
        });
    }

    public float ThermalHeadroom => thermalHeadroom.Value;

    public bool? PowerSaving => powerSaving.Value;

    public GameMode? GameMode => gameMode.Value;

    public void SetGameState(GameState state, bool loading)
    {
        // The state came in API 33, after the manager.
        if (gameManager == null || !OperatingSystem.IsAndroidVersionAtLeast(33))
            return;

        var mode = state switch
        {
            GameState.Gameplay => GameStateMode.GameplayInterruptible,
            GameState.UninterruptibleGameplay => GameStateMode.GameplayUninterruptible,
            GameState.Content => GameStateMode.Content,
            _ => GameStateMode.None
        };

        gameManager.SetGameState(new AndroidGameState(loading, (int)mode));
    }

    // Custom is the player's own mix of resolution and frame rate, which the system applies itself. Unsupported is an app
    // the system doesn't take for a game.
    [SupportedOSPlatform("android31.0")]
    private static GameMode? gameModeOf(AndroidGameMode mode) => mode switch
    {
        AndroidGameMode.Unsupported => null,
        AndroidGameMode.Performance => Platform.GameMode.Performance,
        AndroidGameMode.Battery => Platform.GameMode.Battery,
        _ => Platform.GameMode.Standard
    };

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

    /// <summary>
    /// A value read from the system at most once a second, so it's cheap enough to read every frame. Android answers the
    /// thermal headroom with NaN when asked more often than that, and the others go through binder calls.
    /// </summary>
    private sealed class Polled<T>
    {
        private const long interval_ms = 1000;

        private readonly Func<T, T> read;
        private readonly object gate = new object();
        private T value;

        // Null until the first read, rather than a time long ago, which would overflow when subtracted from now.
        private long? lastRead;

        /// <param name="initial">The value before the first read.</param>
        /// <param name="read">Reads the value, given the last one.</param>
        public Polled(T initial, Func<T, T> read)
        {
            value = initial;
            this.read = read;
        }

        public T Value
        {
            get
            {
                long now = System.Environment.TickCount64;

                lock (gate)
                {
                    if (lastRead == null || now - lastRead >= interval_ms)
                    {
                        lastRead = now;
                        value = read(value);
                    }

                    return value;
                }
            }
        }
    }

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
