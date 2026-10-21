// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;

namespace Sakura.Framework.Tests;

/// <summary>
/// How long this machine really takes to come back from a short timed wait, for tests that measure
/// real-time scheduling and mean nothing where the answer is several times what was asked for.
/// </summary>
/// <remarks>
/// Desktops come back from a 1 ms wait in about 1 ms. Shared CI virtual machines can take five times that:
/// GitHub's macOS runners do, where a test counting how often a 1 ms poll runs sees the idle rate instead.
/// Measured once per process, on the same kind of wait the framework's threads use.
/// </remarks>
internal static class TimerPrecision
{
    private const int samples = 40;

    private static readonly Lazy<double> one_millisecond_wait = new Lazy<double>(measure);

    /// <summary>
    /// The 90th percentile of how long a 1 ms timed wait takes here, in milliseconds.
    /// </summary>
    public static double OneMillisecondWait => one_millisecond_wait.Value;

    /// <summary>
    /// Skips the calling test when a 1 ms timed wait takes longer than <paramref name="maxMilliseconds"/> here.
    /// </summary>
    public static void RequireOneMillisecondWaitWithin(double maxMilliseconds)
    {
        double actual = OneMillisecondWait;

        if (actual > maxMilliseconds)
        {
            Assert.Ignore($"A 1 ms timed wait takes {actual:F1} ms on this machine (90th percentile), more than the {maxMilliseconds} ms this "
                          + "test's timing needs. Shared CI virtual machines are like that; a desktop isn't.");
        }
    }

    private static double measure()
    {
        using var neverSignalled = new AutoResetEvent(false);
        double[] waits = new double[samples];

        for (int i = 0; i < samples; i++)
        {
            long start = Stopwatch.GetTimestamp();
            neverSignalled.WaitOne(1);
            waits[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        Array.Sort(waits);
        return waits[samples * 9 / 10];
    }
}
