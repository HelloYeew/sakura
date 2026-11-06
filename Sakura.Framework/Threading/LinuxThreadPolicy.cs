// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Threading;

/// <summary>
/// What a frame thread asks Linux for at each <see cref="ThreadActivity"/>: its timer slack. The kernel may run a
/// sleeping thread's timer up to its slack late, 50 µs by default, to wake it together with others; in play the thread
/// asks for none, and gives the default back otherwise.
/// </summary>
/// <remarks>
/// <para>
/// Priority is left alone. Raising it (a negative nice) can't be undone without rtkit or a raised RLIMIT_NICE, and a
/// thread that couldn't drop back would keep it after play. Real time is out too: a long frame, such as one compiling a
/// shader, would run into RLIMIT_RTTIME and get the process killed.
/// </para>
/// <para>
/// The first policy made logs what the system allows, so a player's log says whether a custom slice or a reversible
/// nice could be used.
/// </para>
/// </remarks>
internal sealed class LinuxThreadPolicy : IThreadPolicy
{
    private const int pr_set_timerslack = 29;
    private const int pr_get_timerslack = 30;
    private const int rlimit_nice = 13;

    /// <summary>
    /// The slack asked for in play, in nanoseconds. 0 would mean "the default" to the kernel, so this is the smallest
    /// there is.
    /// </summary>
    private const ulong tight_slack_ns = 1;

    private static int reported;

    private readonly string threadName;

    /// <param name="threadName">The thread's name, for logging.</param>
    [SupportedOSPlatform("linux")]
    public LinuxThreadPolicy(string threadName)
    {
        this.threadName = threadName;

        if (Interlocked.Exchange(ref reported, 1) == 0)
            Logger.Verbose($"Linux scheduling: {describeSystem()}");
    }

    /// <summary>
    /// The timer slack a thread asks for at a level, in nanoseconds; 0 gives back the default.
    /// </summary>
    internal static ulong TimerSlackFor(ThreadActivity activity) => activity == ThreadActivity.Tight ? tight_slack_ns : 0;

    public string Apply(ThreadActivity activity)
    {
        if (prctl(pr_set_timerslack, (nuint)TimerSlackFor(activity), 0, 0, 0) != 0)
        {
            Logger.Debug($"{threadName} keeps its timer slack: prctl failed (error {Marshal.GetLastPInvokeError()}).");
            return "timer slack unchanged";
        }

        return $"timer slack {describeSlack(prctl(pr_get_timerslack, 0, 0, 0, 0))}";
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

    private static string describeSlack(long nanoseconds) => nanoseconds < 1000 ? $"{nanoseconds} ns" : $"{nanoseconds / 1000.0:0.#} µs";

    private static string describeSystem()
    {
        string kernel = read("/proc/sys/kernel/osrelease") ?? "unknown";
        string schedExt = read("/sys/kernel/sched_ext/state") ?? "not built in";
        string schedExtOps = read("/sys/kernel/sched_ext/root/ops") ?? string.Empty;
        string cpufreq = $"{read("/sys/devices/system/cpu/cpu0/cpufreq/scaling_driver") ?? "unknown"}/{read("/sys/devices/system/cpu/cpu0/cpufreq/scaling_governor") ?? "unknown"}";

        string nice = getrlimit(rlimit_nice, out var limit) == 0
            ? $"RLIMIT_NICE {describeLimit(limit.Current)} soft / {describeLimit(limit.Maximum)} hard"
            : "RLIMIT_NICE unknown";

        return $"kernel {kernel}, sched_ext {schedExt}{(schedExtOps.Length > 0 ? $" ({schedExtOps})" : string.Empty)}, {nice}, "
               + $"default timer slack {describeSlack(prctl(pr_get_timerslack, 0, 0, 0, 0))}, cpufreq {cpufreq}";
    }

    // RLIMIT_NICE's ceiling is 20 - limit: 0 allows no raise at all, 40 allows nice -20.
    private static string describeLimit(ulong limit) => limit == ulong.MaxValue ? "unlimited" : $"{limit} (nice down to {20 - (long)Math.Min(limit, 40)})";

    private static string? read(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ResourceLimit
    {
        public ulong Current;
        public ulong Maximum;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int prctl(int option, nuint argument2, nuint argument3, nuint argument4, nuint argument5);

    [DllImport("libc", SetLastError = true)]
    private static extern int getrlimit(int resource, out ResourceLimit limit);
}
