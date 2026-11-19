// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.InteropServices;
using System.Threading;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Threading;

/// <summary>
/// A sleep on Linux and Android that another thread can end early: a futex wait on a word of its own, with an absolute
/// deadline on the monotonic clock. <see cref="Wake"/> sets the word and wakes the waiter.
/// </summary>
/// <remarks>
/// A futex's timeout is late by at most the thread's timer slack, exactly as nanosleep's is, so a frame thread in play keeps
/// the 1 ns slack its policy asks for. A poll-family wait would add a tenth of a percent of its timeout on top, 8 µs on a
/// 120 Hz frame.
/// </remarks>
internal sealed unsafe class LinuxFutexSleep : INativeSleep
{
    private const int clock_monotonic = 1;
    private const int futex_wake = 1;
    private const int futex_wait_bitset = 9;
    private const int futex_private_flag = 128;
    private const int futex_bitset_match_any = -1;
    private const int eintr = 4;
    private const int eagain = 11;
    private const int etimedout = 110;
    private const long ns_per_second = 1_000_000_000L;

    /// <summary>
    /// The futex system call's number on this architecture: x64's and 32-bit x86 and Arm's own, else the generic table's. 0 on
    /// an architecture with a table of its own that isn't listed, which then sleeps with <see cref="UnixNativeSleep"/>.
    /// </summary>
    private static readonly nint futex_number = RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => 202,
        Architecture.X86 or Architecture.Arm or Architecture.Armv6 => 240,
        Architecture.Arm64 or Architecture.RiscV64 or Architecture.LoongArch64 => 98,
        _ => 0
    };

    /// <summary>
    /// Whether the platform has the call. Asked once, with a wake that no one waits for.
    /// </summary>
    public static bool IsAvailable { get; } = probe();

    /// <summary>
    /// 1 from a wake until a sleep takes it. Outside the managed heap, since the kernel keys a futex on its address.
    /// </summary>
    private readonly int* word;

    public LinuxFutexSleep()
    {
        word = (int*)NativeMemory.AllocZeroed(sizeof(int));
    }

    public SleepResult Sleep(TimeSpan duration)
    {
        long nanoseconds = (long)duration.TotalNanoseconds;

        if (nanoseconds <= 0)
            return SleepResult.Slept;

        if (clock_gettime(clock_monotonic, out var now) != 0)
            return SleepResult.Failed;

        // Absolute, so a wait that a signal interrupts resumes towards the same moment.
        long deadlineNanoseconds = now.Seconds * ns_per_second + now.NanoSeconds + nanoseconds;

        var deadline = new TimeSpec
        {
            Seconds = (nint)(deadlineNanoseconds / ns_per_second),
            NanoSeconds = (nint)(deadlineNanoseconds % ns_per_second)
        };

        while (true)
        {
            if (Interlocked.Exchange(ref *word, 0) != 0)
                return SleepResult.Woken;

            // Returns at once if a wake set the word after it was read above, so that wake isn't lost.
            if (futex(futex_number, word, futex_wait_bitset | futex_private_flag, 0, &deadline, null, futex_bitset_match_any) == 0)
                continue;

            switch (Marshal.GetLastPInvokeError())
            {
                case etimedout:
                    return SleepResult.Slept;

                case eagain:
                case eintr:
                    continue;

                default:
                    return SleepResult.Failed;
            }
        }
    }

    public void Wake()
    {
        Interlocked.Exchange(ref *word, 1);
        _ = futex(futex_number, word, futex_wake | futex_private_flag, 1, null, null, 0);
    }

    public void Dispose() => NativeMemory.Free(word);

    private static bool probe()
    {
        if (!(OperatingSystem.IsLinux() || OperatingSystem.IsAndroid()) || futex_number == 0)
            return false;

        NativeLibraryResolver.Install();

        try
        {
            int unused = 0;
            return futex(futex_number, &unused, futex_wake | futex_private_flag, 1, null, null, 0) == 0 && clock_gettime(clock_monotonic, out _) == 0;
        }
        catch
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TimeSpec
    {
        public nint Seconds;
        public nint NanoSeconds;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int clock_gettime(int clock, out TimeSpec time);

    /// <summary>
    /// The futex call, through libc's syscall(), which C libraries have no wrapper for. Every argument is an integer or a
    /// pointer, which the variadic syscall() takes in the same registers as fixed ones on Linux's calling conventions.
    /// </summary>
    [DllImport("libc", EntryPoint = "syscall", SetLastError = true)]
    private static extern nint futex(nint number, int* address, int operation, int value, TimeSpec* timeout, int* address2, int value3);
}
