// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.InteropServices;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Threading;

/// <summary>
/// What a thread asks macOS and iOS for at each <see cref="ThreadActivity"/>: a fixed priority, and for the frame
/// threads a work interval that tells the performance controller when each frame is due.
/// </summary>
/// <remarks>
/// <para>
/// .NET gives every thread it starts a fixed priority, which takes it out of the system's quality-of-service classes
/// for good, so the thread sets a fixed priority of its own instead. The scheduler groups threads by priority: above 37
/// a thread is never kept waiting behind other apps' work, while at 31 to 37, where .NET's priorities fall, it can wait
/// tens of milliseconds under load.
/// </para>
/// <para>
/// The work interval decides the cores and the clock speed. Without one, an idle Mac runs a 1 ms frame in about 2 ms on
/// efficient cores and slow clocks, unevenly. A deadline a quarter of a frame after the start gets the fast cores at
/// full speed; a deadline of the next frame lets the efficient cores take it, as long as they finish in time.
/// </para>
/// </remarks>
internal sealed class DarwinThreadPolicy : IThreadPolicy
{
    private const string lib_system = "/usr/lib/libSystem.dylib";
    private const string audio_toolbox = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";

    /// <summary>
    /// The same as the main thread's while the app is focused.
    /// </summary>
    private const int tight_priority = 47;

    /// <summary>
    /// The lowest priority other apps' work never holds back.
    /// </summary>
    private const int smooth_priority = 38;

    /// <summary>
    /// Behind the focused app, but still ahead of builds and other default-priority work.
    /// </summary>
    private const int background_priority = 37;

    /// <summary>
    /// The system's default for an app's threads.
    /// </summary>
    private const int hidden_priority = 31;

    /// <summary>
    /// How much of the frame period a tight work interval gives the frame's work.
    /// </summary>
    private const double tight_deadline_fraction = 0.25;

    private const int sched_other = 1;
    private const int thread_extended_info = 5;
    private const int thread_extended_info_count = 28;
    private const int base_priority_index = 10;
    private const uint os_clock_mach_absolute_time = 32;

    // os_workgroup_join_token_s is 40 bytes on 64-bit platforms.
    private const int join_token_size = 64;

    /// <summary>
    /// Whether the platform has these.
    /// </summary>
    public static bool IsAvailable => OperatingSystem.IsMacOS() || OperatingSystem.IsIOS();

    private static readonly double mach_units_per_millisecond = machUnitsPerMillisecond();

    private readonly ThreadRole role;
    private readonly string threadName;
    private bool useWorkInterval;

    private ThreadActivity activity;
    private IntPtr workInterval;
    private IntPtr joinToken;
    private bool intervalStarted;

    /// <param name="role">Which thread this is for.</param>
    /// <param name="threadName">The thread's name, for logging.</param>
    /// <param name="useWorkInterval">Whether to tell the performance controller when each frame is due.</param>
    public DarwinThreadPolicy(ThreadRole role, string threadName, bool useWorkInterval)
    {
        this.role = role;
        this.threadName = threadName;
        this.useWorkInterval = useWorkInterval;
    }

    /// <summary>
    /// The fixed priority a thread asks for at a level.
    /// </summary>
    internal static int PriorityFor(ThreadRole role, ThreadActivity activity) => activity switch
    {
        ThreadActivity.Tight => tight_priority,
        ThreadActivity.Smooth => smooth_priority,

        // The audio thread keeps the music's state moving for a player who listens from another app.
        ThreadActivity.Background => role == ThreadRole.Audio ? smooth_priority : background_priority,
        _ => hidden_priority
    };

    public string Apply(ThreadActivity activity)
    {
        this.activity = activity;

        int requested = PriorityFor(role, activity);
        int error = SetOwnPriority(requested);
        string applied = error == 0 ? $"priority {readBasePriority()}" : $"priority unchanged (error {error} asking for {requested})";

        if (!useWorkInterval)
            return applied;

        bool wantsInterval = activity is ThreadActivity.Tight or ThreadActivity.Smooth;

        if (wantsInterval && joinToken == IntPtr.Zero)
            joinWorkInterval();
        else if (!wantsInterval && joinToken != IntPtr.Zero)
            leaveWorkInterval();

        if (joinToken == IntPtr.Zero)
            return applied;

        return activity == ThreadActivity.Tight ? $"{applied}, tight work interval" : $"{applied}, relaxed work interval";
    }

    public void BeginFrame(double frameMilliseconds)
    {
        if (joinToken == IntPtr.Zero || frameMilliseconds <= 0)
            return;

        double fraction = activity == ThreadActivity.Tight ? tight_deadline_fraction : 1;
        ulong start = mach_absolute_time();
        ulong deadline = start + (ulong)(frameMilliseconds * fraction * mach_units_per_millisecond);

        intervalStarted = os_workgroup_interval_start(workInterval, start, deadline, IntPtr.Zero) == 0;
    }

    public void EndFrame()
    {
        if (!intervalStarted)
            return;

        os_workgroup_interval_finish(workInterval, IntPtr.Zero);
        intervalStarted = false;
    }

    public void Dispose()
    {
        EndFrame();
        leaveWorkInterval();

        if (workInterval != IntPtr.Zero)
        {
            os_release(workInterval);
            workInterval = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Sets the calling thread's fixed priority, for threads that keep one priority whatever the level.
    /// </summary>
    /// <returns>0, or the error pthread_setschedparam returned.</returns>
    internal static unsafe int SetOwnPriority(int priority)
    {
        var parameters = new SchedParam
        {
            Priority = priority
        };

        return pthread_setschedparam(pthread_self(), sched_other, &parameters);
    }

    private void joinWorkInterval()
    {
        if (workInterval == IntPtr.Zero)
        {
            workInterval = AudioWorkIntervalCreate(threadName, os_clock_mach_absolute_time, IntPtr.Zero);

            if (workInterval == IntPtr.Zero)
            {
                Logger.Debug($"{threadName} has no work interval: the system didn't make one.");
                useWorkInterval = false;
                return;
            }
        }

        IntPtr token = Marshal.AllocHGlobal(join_token_size);
        int error = os_workgroup_join(workInterval, token);

        if (error != 0)
        {
            Marshal.FreeHGlobal(token);
            Logger.Debug($"{threadName} has no work interval: joining it failed (error {error}).");
            useWorkInterval = false;
            return;
        }

        joinToken = token;
    }

    private void leaveWorkInterval()
    {
        if (joinToken == IntPtr.Zero)
            return;

        EndFrame();
        os_workgroup_leave(workInterval, joinToken);
        Marshal.FreeHGlobal(joinToken);
        joinToken = IntPtr.Zero;
    }

    private static unsafe int readBasePriority()
    {
        int* info = stackalloc int[thread_extended_info_count];
        int count = thread_extended_info_count;

        return thread_info(pthread_mach_thread_np(pthread_self()), thread_extended_info, info, ref count) == 0 ? info[base_priority_index] : -1;
    }

    private static double machUnitsPerMillisecond()
    {
        if (!IsAvailable || mach_timebase_info(out var timebase) != 0 || timebase.Numerator == 0)
            return 1_000_000;

        return 1_000_000.0 * timebase.Denominator / timebase.Numerator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SchedParam
    {
        public int Priority;
        public int Opaque;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MachTimebaseInfo
    {
        public uint Numerator;
        public uint Denominator;
    }

    [DllImport(lib_system)]
    private static extern IntPtr pthread_self();

    [DllImport(lib_system)]
    private static extern unsafe int pthread_setschedparam(IntPtr thread, int policy, SchedParam* parameters);

    [DllImport(lib_system)]
    private static extern uint pthread_mach_thread_np(IntPtr thread);

    [DllImport(lib_system)]
    private static extern unsafe int thread_info(uint thread, int flavor, int* info, ref int count);

    [DllImport(lib_system)]
    private static extern ulong mach_absolute_time();

    [DllImport(lib_system)]
    private static extern int mach_timebase_info(out MachTimebaseInfo info);

    [DllImport(audio_toolbox)]
    private static extern IntPtr AudioWorkIntervalCreate([MarshalAs(UnmanagedType.LPUTF8Str)] string name, uint clock, IntPtr attributes);

    [DllImport(lib_system)]
    private static extern int os_workgroup_join(IntPtr workgroup, IntPtr token);

    [DllImport(lib_system)]
    private static extern void os_workgroup_leave(IntPtr workgroup, IntPtr token);

    [DllImport(lib_system)]
    private static extern int os_workgroup_interval_start(IntPtr workgroup, ulong start, ulong deadline, IntPtr data);

    [DllImport(lib_system)]
    private static extern int os_workgroup_interval_finish(IntPtr workgroup, IntPtr data);

    [DllImport(lib_system)]
    private static extern void os_release(IntPtr instance);
}
