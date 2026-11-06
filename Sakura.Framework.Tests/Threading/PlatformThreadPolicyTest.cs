// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Threading;
using NUnit.Framework;
using Sakura.Framework.Threading;

namespace Sakura.Framework.Tests.Threading;

/// <summary>
/// Covers what Windows and Linux threads ask for at each level. The maps are checked everywhere, the system calls only
/// on their own platform, each on a thread of its own so the test runner's threads keep their settings.
/// </summary>
[TestFixture]
public class PlatformThreadPolicyTest
{
    [TestCase(ThreadActivity.Tight, false)]
    [TestCase(ThreadActivity.Smooth, false)]
    [TestCase(ThreadActivity.Background, false)]
    [TestCase(ThreadActivity.Hidden, true)]
    public void WindowsAsksForEcoQoSOnlyWhileHidden(ThreadActivity activity, bool eco)
    {
        Assert.That(WindowsThreadPolicy.UsesEcoQoS(activity), Is.EqualTo(eco));
    }

    /// <summary>
    /// Focused, sleeps elsewhere in the process may need the fine resolution. Out of the user's way, it goes back.
    /// </summary>
    [TestCase(ThreadActivity.Tight, 1)]
    [TestCase(ThreadActivity.Smooth, 1)]
    [TestCase(ThreadActivity.Background, 0)]
    [TestCase(ThreadActivity.Hidden, 0)]
    public void WindowsHoldsTheFineTimerResolutionOnlyWhileFocused(ThreadActivity activity, int period)
    {
        Assert.That(WindowsTimerResolution.PeriodFor(activity), Is.EqualTo(period));
    }

    [TestCase(ThreadActivity.Tight, 1UL)]
    [TestCase(ThreadActivity.Smooth, 0UL)]
    [TestCase(ThreadActivity.Background, 0UL)]
    [TestCase(ThreadActivity.Hidden, 0UL)]
    public void LinuxDropsTimerSlackOnlyInPlay(ThreadActivity activity, ulong slack)
    {
        Assert.That(LinuxThreadPolicy.TimerSlackFor(activity), Is.EqualTo(slack));
    }

    /// <summary>
    /// Raised to the urgent display priority only in play, and back to where it started otherwise. A thread that
    /// started higher than that keeps its own.
    /// </summary>
    [TestCase(ThreadActivity.Tight, -4, -8)]
    [TestCase(ThreadActivity.Smooth, -4, -4)]
    [TestCase(ThreadActivity.Background, -4, -4)]
    [TestCase(ThreadActivity.Hidden, -4, -4)]
    [TestCase(ThreadActivity.Tight, 0, -8)]
    [TestCase(ThreadActivity.Tight, -10, -10)]
    public void AndroidRaisesFrameThreadsOnlyInPlay(ThreadActivity activity, int startingNice, int nice)
    {
        Assert.That(AndroidThreadPolicy.NiceFor(activity, startingNice), Is.EqualTo(nice));
    }

    [Test]
    [SupportedOSPlatform("windows")]
    public void AWindowsThreadGetsEcoQoSAndGivesItBack()
    {
        Assume.That(OperatingSystem.IsWindows(), Is.True, "Windows only");

        string[] applied = onOwnThread(() =>
        {
            using var policy = new WindowsThreadPolicy("EcoQoSTestThread");

            return new[] { policy.Apply(ThreadActivity.Hidden), policy.Apply(ThreadActivity.Tight) };
        });

        Assert.That(applied, Is.EqualTo(new[] { "EcoQoS", "QoS left to the system" }));
    }

    [Test]
    [SupportedOSPlatform("linux")]
    public void ALinuxThreadDropsItsTimerSlackAndGivesItBack()
    {
        Assume.That(OperatingSystem.IsLinux(), Is.True, "Linux only");

        string[] applied = onOwnThread(() =>
        {
            using var policy = new LinuxThreadPolicy("SlackTestThread");

            return new[] { policy.Apply(ThreadActivity.Tight), policy.Apply(ThreadActivity.Smooth) };
        });

        Assert.That(applied[0], Is.EqualTo("timer slack 1 ns"));
        Assert.That(applied[1], Does.StartWith("timer slack ").And.Not.EqualTo("timer slack 1 ns"), "back to the default");
    }

    private static T onOwnThread<T>(Func<T> work)
    {
        T result = default!;
        Exception? failure = null;
        int done = 0;

        new Thread(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception e)
            {
                failure = e;
            }

            Volatile.Write(ref done, 1);
        }).Start();

        var timeout = Stopwatch.StartNew();

        while (Volatile.Read(ref done) == 0 && timeout.ElapsedMilliseconds < 30_000)
            Thread.Sleep(5);

        if (failure != null)
            throw failure;

        Assert.That(done, Is.EqualTo(1), "timed out");
        return result;
    }
}
