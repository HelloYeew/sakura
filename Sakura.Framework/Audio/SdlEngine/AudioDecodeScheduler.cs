// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// The single background thread that keeps every playing track's buffer topped up.
/// </summary>
internal sealed class AudioDecodeScheduler : IDisposable
{
    /// <summary>
    /// How long to idle when no source wanted work.
    /// </summary>
    /// <remarks>
    /// Sized against the buffers being kept, not as a reflex. Both kinds of source hold 500 ms and ask
    /// for a refill once they are down to 400 ms, so a source that started wanting work just after a
    /// poll waits at most this long, and is still holding 375 ms when it is served — at double speed,
    /// 350. Everything that needs the thread sooner wakes it instead of waiting: a new source, a seek,
    /// playback starting. At the old 5 ms the thread woke 200 times a second to find every ring full.
    /// </remarks>
    private static readonly TimeSpan idle_delay = TimeSpan.FromMilliseconds(25);

    /// <summary>
    /// How long to wait when a source wanted work but could not do it yet.
    /// </summary>
    /// <remarks>
    /// A source in that state is not idle, it is blocked on the audio thread
    /// </remarks>
    private static readonly TimeSpan blocked_delay = TimeSpan.FromMilliseconds(1);

    /// <summary>
    /// How long a source may stay blocked before it's asked at the idle rate instead: longer than any output takes to
    /// apply a discard, which it does at its next callback.
    /// </summary>
    private static readonly TimeSpan blocked_for_long = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Maximum consecutive pumps for one source before moving on.
    /// </summary>
    private const int max_pumps_per_source = 8;

    private readonly List<IDecodeSource> sources = new List<IDecodeSource>();
    private readonly Lock sync = new Lock();
    private readonly AutoResetEvent wakeup = new AutoResetEvent(false);
    private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
    private readonly Thread thread;

    private IDecodeSource[] snapshot = Array.Empty<IDecodeSource>();

    public AudioDecodeScheduler()
    {
        thread = new Thread(run)
        {
            Name = "SdlAudioDecode",
            IsBackground = true,

            // Above normal but below the mix thread: falling behind here is an audible dropout, but
            // it must never be the thing that delays the mixer itself.
            Priority = ThreadPriority.AboveNormal
        };

        thread.Start();
    }

    public void Register(IDecodeSource source)
    {
        lock (sync)
        {
            sources.Add(source);
            snapshot = sources.ToArray();
        }

        // A newly started track has an empty buffer, do not make it wait out an idle delay.
        wakeup.Set();
    }

    public void Unregister(IDecodeSource source)
    {
        lock (sync)
        {
            sources.Remove(source);
            snapshot = sources.ToArray();
        }
    }

    /// <summary>
    /// Nudges the decode thread, for when a source suddenly needs work. A seek, or playback
    /// starting rather than waiting for the next idle poll.
    /// </summary>
    public void Wake() => wakeup.Set();

    private void run()
    {
        if (OperatingSystem.IsAndroid())
            raiseOnAndroid();

        long blockedSince = 0;

        while (!cancellation.IsCancellationRequested)
        {
            bool didWork = false;
            bool blocked = false;

            // registration during a pass is picked up next time
            // around, which is soon enough and keeps the lock off the decode path.
            var current = snapshot;

            foreach (var source in current)
            {
                if (cancellation.IsCancellationRequested)
                    break;

                try
                {
                    for (int i = 0; i < max_pumps_per_source && source.WantsDecode; i++)
                    {
                        if (!source.PumpDecode())
                        {
                            // A pass that did nothing but still wants to decode is waiting on something
                            // other than this thread, which in practice is the audio thread applying a
                            // seek's discard. Asked again after a short wait rather than a full idle
                            // one, a source that has simply finished stops wanting decoding and does
                            // not land here.
                            blocked |= source.WantsDecode;
                            break;
                        }

                        didWork = true;
                    }
                }
                catch (Exception e)
                {
                    // One bad file must not take the decoded thread down with it and silence
                    // everything else that is playing.
                    Logger.Error($"[AudioDecodeScheduler] Decoding failed for a source; dropping it.", e);
                    Unregister(source);
                }
            }

            if (didWork || !blocked)
                blockedSince = 0;
            else if (blockedSince == 0)
                blockedSince = Stopwatch.GetTimestamp();

            // A discard lands within a callback or two of any output that's running. One still waiting after that
            // waits on an output that isn't (stopped in the background, or gone), and asking a thousand times a second
            // for as long as that lasts would only drain the battery.
            bool longBlocked = blockedSince != 0 && Stopwatch.GetElapsedTime(blockedSince) > blocked_for_long;

            if (!didWork)
                wakeup.WaitOne(blocked && !longBlocked ? blocked_delay : idle_delay);
        }
    }

    // Android's THREAD_PRIORITY_URGENT_DISPLAY, which the frame threads are raised to (AndroidPerformance).
    private const int android_frame_thread_priority = -8;

    // setpriority's PRIO_PROCESS, which on Linux, given a thread's ID, applies to that thread alone.
    private const int prio_process = 0;

    /// <summary>
    /// Puts this thread level with the frame threads. Thread.Priority changes nothing on Android, so it stayed below
    /// them, and a frame thread that kept a core under load drained the half second it keeps ahead into a gap.
    /// </summary>
    private static void raiseOnAndroid()
    {
        if (setpriority(prio_process, gettid(), android_frame_thread_priority) != 0)
            Logger.Verbose($"The audio decode thread stays at its priority: setpriority failed (error {Marshal.GetLastPInvokeError()}).");
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int setpriority(int which, int who, int priority);

    [DllImport("libc")]
    private static extern int gettid();

    public void Dispose()
    {
        cancellation.Cancel();
        wakeup.Set();

        if (!thread.Join(TimeSpan.FromSeconds(2)))
            Logger.Error("[AudioDecodeScheduler] Decode thread did not exit in time.");

        lock (sync)
        {
            sources.Clear();
            snapshot = Array.Empty<IDecodeSource>();
        }

        wakeup.Dispose();
        cancellation.Dispose();
    }
}
