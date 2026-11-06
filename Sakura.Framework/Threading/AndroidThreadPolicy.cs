// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Threading;

/// <summary>
/// What a frame thread asks Android for at each <see cref="ThreadActivity"/>: in play, Android's urgent display priority
/// (nice −8), ahead of the app's background work; anywhere else, the priority it started with. Android never lowers a
/// raised priority by itself, so without this a game would hold it through its menus too.
/// </summary>
/// <remarks>
/// <para>
/// Every thread the app makes starts at the display priority (−4), which SDL gives its own thread and every thread made
/// from it inherits. Chromium stops its display threads there on Pixels, where it saw input jank below it with
/// performance hint sessions; −8 here is kept for play until a phone run compares the two.
/// </para>
/// <para>
/// The audio decode thread follows whichever frame thread is higher (<see cref="FrameNice"/>): below them, a frame
/// thread that kept a core busy drained the half second of sound it decodes ahead into a gap.
/// </para>
/// </remarks>
internal sealed class AndroidThreadPolicy : IThreadPolicy
{
    /// <summary>
    /// Android's THREAD_PRIORITY_URGENT_DISPLAY: still behind audio (−16).
    /// </summary>
    private const int urgent_display_nice = -8;

    /// <summary>
    /// The display priority every app thread starts at, for when a thread's own can't be read.
    /// </summary>
    private const int display_nice = -4;

    // setpriority's PRIO_PROCESS, which on Linux, given a thread's ID, applies to that thread alone.
    private const int prio_process = 0;

    // sched_setscheduler's SCHED_OTHER, the normal policy, with SCHED_RESET_ON_FORK: a thread made by one with a raised
    // priority starts at the default instead of inheriting it, so thread-pool workers and decoders started from a frame
    // thread don't end up level with it.
    private const int sched_other_reset_on_fork = 0x40000000;

    private static int updateNice = int.MaxValue;
    private static int drawNice = int.MaxValue;

    private readonly ThreadRole role;
    private readonly string threadName;
    private readonly int threadId;
    private readonly int startingNice;

    /// <param name="role">Which thread this is for.</param>
    /// <param name="threadName">The thread's name, for logging.</param>
    [SupportedOSPlatform("android")]
    public AndroidThreadPolicy(ThreadRole role, string threadName)
    {
        this.role = role;
        this.threadName = threadName;
        threadId = gettid();
        startingNice = ReadNice(threadId) ?? display_nice;

        var parameters = new SchedParam();

        if (sched_setscheduler(threadId, sched_other_reset_on_fork, ref parameters) != 0)
            Logger.Verbose($"Threads {threadName} makes will inherit its priority: sched_setscheduler failed (error {Marshal.GetLastPInvokeError()}).");
    }

    /// <summary>
    /// The nice value a frame thread asks for at a level, given the one it started with.
    /// </summary>
    internal static int NiceFor(ThreadActivity activity, int startingNice) => activity == ThreadActivity.Tight ? Math.Min(urgent_display_nice, startingNice) : startingNice;

    /// <summary>
    /// The nice value of the higher of the update and draw threads, or null before either has applied a level.
    /// </summary>
    internal static int? FrameNice
    {
        get
        {
            int nice = Math.Min(Volatile.Read(ref updateNice), Volatile.Read(ref drawNice));
            return nice == int.MaxValue ? null : nice;
        }
    }

    public string Apply(ThreadActivity activity)
    {
        int nice = NiceFor(activity, startingNice);

        if (setpriority(prio_process, threadId, nice) != 0)
        {
            Logger.Debug($"{threadName} keeps its priority: setpriority({nice}) failed (error {Marshal.GetLastPInvokeError()}).");
            return "priority unchanged";
        }

        if (role == ThreadRole.Update)
            Volatile.Write(ref updateNice, nice);
        else if (role == ThreadRole.Draw)
            Volatile.Write(ref drawNice, nice);

        return $"nice {ReadNice(threadId)?.ToString() ?? "unknown"}";
    }

    public void BeginFrame(double frameMilliseconds)
    {
    }

    public void EndFrame()
    {
    }

    public void Dispose()
    {
    }

    /// <summary>
    /// Sets the calling thread's nice value to <see cref="FrameNice"/> when it changed since <paramref name="applied"/>.
    /// Cheap enough for every pass of a loop.
    /// </summary>
    /// <param name="applied">The value this thread last set, which this updates.</param>
    /// <param name="fallback">The value to use before any frame thread has applied a level.</param>
    internal static void FollowFrameThreads(ref int? applied, int fallback)
    {
        int nice = FrameNice ?? fallback;

        if (nice == applied)
            return;

        applied = nice;

        if (setpriority(prio_process, gettid(), nice) != 0)
            Logger.Debug($"A thread following the frame threads keeps its priority: setpriority({nice}) failed (error {Marshal.GetLastPInvokeError()}).");
    }

    /// <summary>
    /// A thread's nice value, or null where it can't be read.
    /// </summary>
    internal static int? ReadNice(int threadId)
    {
        // -1 is also a valid nice value, so only errno tells a failure apart.
        Marshal.SetLastPInvokeError(0);
        int nice = getpriority(prio_process, threadId);

        return nice == -1 && Marshal.GetLastPInvokeError() != 0 ? null : nice;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SchedParam
    {
        public int Priority;
    }

    [DllImport("libc")]
    private static extern int gettid();

    [DllImport("libc", SetLastError = true)]
    private static extern int getpriority(int which, int who);

    [DllImport("libc", SetLastError = true)]
    private static extern int setpriority(int which, int who, int priority);

    [DllImport("libc", SetLastError = true)]
    private static extern int sched_setscheduler(int threadId, int policy, ref SchedParam parameters);
}
