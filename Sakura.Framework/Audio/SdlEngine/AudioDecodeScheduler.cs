// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Threading;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// The single background thread that keeps every playing track's buffer topped up.
/// </summary>
internal sealed class AudioDecodeScheduler : IDisposable
{
    /// <summary>
    /// How long to idle when no source wanted work. Short relative to the buffer being maintained,
    /// so the buffer never gets close to empty through scheduling alone.
    /// </summary>
    private static readonly TimeSpan idle_delay = TimeSpan.FromMilliseconds(5);

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
        while (!cancellation.IsCancellationRequested)
        {
            bool didWork = false;

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
                            break;

                        didWork = true;
                    }
                }
                catch (Exception e)
                {
                    // One bad file must not take the decode thread down with it and silence
                    // everything else that is playing.
                    Logger.Error($"[AudioDecodeScheduler] Decoding failed for a source; dropping it.", e);
                    Unregister(source);
                }
            }

            if (!didWork)
                wakeup.WaitOne(idle_delay);
        }
    }

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
