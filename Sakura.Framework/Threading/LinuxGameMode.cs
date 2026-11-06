// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.InteropServices;
using System.Threading;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Threading;

/// <summary>
/// Feral's GameMode on Linux, asked for while a game is in play (<see cref="ThreadActivity.Tight"/>) and given back
/// otherwise. GameMode applies the user's own settings for games: the CPU governor, I/O priority, the GPU's performance
/// mode, a screensaver inhibit, and any scripts they set up. See <see cref="Platform.HostOptions.UseLinuxGameMode"/>.
/// </summary>
/// <remarks>
/// Each request is a D-Bus round trip, so it goes out on a worker thread rather than on whichever thread changed the
/// level, which may be the update thread as a song starts. Ending waits a moment, so a focus blip or the gap between two
/// songs doesn't end GameMode and start it again, running the user's scripts each time.
/// </remarks>
internal sealed class LinuxGameMode : IDisposable
{
    private const string library = "libgamemode.so.0";

    private static readonly TimeSpan end_delay = TimeSpan.FromSeconds(2);

    private readonly Func<int> requestStart;
    private readonly Func<int> requestEnd;
    private readonly Func<string?> describeError;
    private readonly TimeSpan endDelay;
    private readonly Lock gate = new Lock();

    private bool wanted;
    private bool active;
    private bool working;
    private bool disposed;

    /// <param name="requestStart">Asks for GameMode: 0 on success.</param>
    /// <param name="requestEnd">Gives it back: 0 on success.</param>
    /// <param name="describeError">Why the last request failed, if the library says.</param>
    /// <param name="endDelay">How long the game must stay out of play before GameMode ends.</param>
    internal LinuxGameMode(Func<int> requestStart, Func<int> requestEnd, Func<string?> describeError, TimeSpan endDelay)
    {
        this.requestStart = requestStart;
        this.requestEnd = requestEnd;
        this.describeError = describeError;
        this.endDelay = endDelay;
    }

    /// <summary>
    /// Whether GameMode's client library can be loaded.
    /// </summary>
    public static bool IsInstalled
    {
        get
        {
            if (!NativeLibrary.TryLoad(library, out IntPtr handle))
                return false;

            NativeLibrary.Free(handle);
            return true;
        }
    }

    /// <summary>
    /// Loads GameMode's client library, or returns null, saying why in the log, where it isn't installed.
    /// </summary>
    public static unsafe LinuxGameMode? Open()
    {
        if (!NativeLibrary.TryLoad(library, out IntPtr handle))
        {
            Logger.Verbose("GameMode was asked for, but isn't installed (no libgamemode.so.0).");
            return null;
        }

        if (!NativeLibrary.TryGetExport(handle, "real_gamemode_request_start", out IntPtr start) || !NativeLibrary.TryGetExport(handle, "real_gamemode_request_end", out IntPtr end))
        {
            Logger.Verbose("GameMode was asked for, but its library has no request functions.");
            NativeLibrary.Free(handle);
            return null;
        }

        NativeLibrary.TryGetExport(handle, "real_gamemode_error_string", out IntPtr error);

        return new LinuxGameMode(
            () => ((delegate* unmanaged<int>)start)(),
            () => ((delegate* unmanaged<int>)end)(),
            () => error == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(((delegate* unmanaged<IntPtr>)error)()),
            end_delay);
    }

    /// <summary>
    /// Whether GameMode is held now, as far as this side knows.
    /// </summary>
    internal bool IsActive
    {
        get
        {
            lock (gate)
                return active;
        }
    }

    /// <summary>
    /// Asks for GameMode or gives it back. Returns at once; safe from any thread.
    /// </summary>
    public void Want(bool on)
    {
        lock (gate)
        {
            wanted = on;

            if (working || disposed)
                return;

            working = true;
        }

        ThreadPool.UnsafeQueueUserWorkItem(_ => apply(), null);
    }

    /// <summary>
    /// Gives GameMode back if it's held. Called as the host shuts down, so it ends now rather than after the delay.
    /// </summary>
    public void Dispose()
    {
        bool wasActive;

        lock (gate)
        {
            disposed = true;
            wasActive = active;
            active = false;
        }

        if (wasActive)
            request(false);
    }

    private void apply()
    {
        while (true)
        {
            bool target;

            lock (gate)
            {
                target = wanted;

                if (target == active || disposed)
                {
                    working = false;
                    return;
                }
            }

            if (!target)
            {
                Thread.Sleep(endDelay);

                // Back in play meanwhile: the next pass finds nothing to do.
                lock (gate)
                {
                    if (wanted)
                        continue;
                }
            }

            request(target);

            lock (gate)
            {
                if (!disposed)
                    active = target;
            }
        }
    }

    private void request(bool on)
    {
        int result = on ? requestStart() : requestEnd();

        if (result == 0)
            Logger.Verbose(on ? "GameMode on, for play" : "GameMode off");
        else
            Logger.Verbose($"GameMode request to {(on ? "start" : "end")} failed: {describeError() ?? $"error {result}"}");
    }
}
