// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Sakura.Framework.Threading;

namespace Sakura.Framework.Tests.Threading;

/// <summary>
/// Covers when GameMode is asked for and given back: off the calling thread, at once for play, and only after a moment
/// out of it, so the gap between two songs doesn't run the user's GameMode scripts twice.
/// </summary>
[TestFixture]
public class LinuxGameModeTest
{
    private static readonly TimeSpan end_delay = TimeSpan.FromMilliseconds(300);

    [Test]
    public void PlayAsksForGameModeOffTheCallingThread()
    {
        var requests = new Requests();
        using var gameMode = requests.Create();

        gameMode.Want(true);
        waitFor(() => gameMode.IsActive);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(requests.Log, Is.EqualTo(new[] { "start" }));
            Assert.That(requests.Threads.Single(), Is.Not.EqualTo(Environment.CurrentManagedThreadId), "a D-Bus round trip, so not on the caller's thread");
        }
    }

    [Test]
    public void AMomentOutOfPlayKeepsGameMode()
    {
        var requests = new Requests();
        using var gameMode = requests.Create();

        gameMode.Want(true);
        waitFor(() => gameMode.IsActive);

        gameMode.Want(false);
        Thread.Sleep(end_delay / 10);
        gameMode.Want(true);
        Thread.Sleep(end_delay * 3);

        Assert.That(requests.Log, Is.EqualTo(new[] { "start" }));
        Assert.That(gameMode.IsActive, Is.True);
    }

    [Test]
    public void LeavingPlayGivesGameModeBackAfterTheDelay()
    {
        var requests = new Requests();
        using var gameMode = requests.Create();

        gameMode.Want(true);
        waitFor(() => gameMode.IsActive);

        var left = Stopwatch.StartNew();
        gameMode.Want(false);
        waitFor(() => !gameMode.IsActive);

        Assert.That(requests.Log, Is.EqualTo(new[] { "start", "end" }));
        Assert.That(left.Elapsed, Is.GreaterThanOrEqualTo(end_delay * 0.9), "not before the delay");
    }

    [Test]
    public void ShuttingDownGivesGameModeBackAtOnce()
    {
        var requests = new Requests();
        var gameMode = requests.Create();

        gameMode.Want(true);
        waitFor(() => gameMode.IsActive);

        gameMode.Dispose();

        Assert.That(requests.Log, Is.EqualTo(new[] { "start", "end" }));
    }

    private static void waitFor(Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();

        while (!condition() && timeout.ElapsedMilliseconds < 5000)
            Thread.Sleep(5);

        Assert.That(condition(), Is.True, "timed out");
    }

    private sealed class Requests
    {
        public readonly ConcurrentQueue<string> Log = new ConcurrentQueue<string>();
        public readonly ConcurrentQueue<int> Threads = new ConcurrentQueue<int>();

        public LinuxGameMode Create() => new LinuxGameMode(() => record("start"), () => record("end"), () => null, end_delay);

        private int record(string request)
        {
            Log.Enqueue(request);
            Threads.Enqueue(Environment.CurrentManagedThreadId);
            return 0;
        }
    }
}
