// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Tests.Platform;

/// <summary>
/// The input loop waits for events for up to a refresh, so what another thread asks of it must be able to end the wait:
/// a headless host's tests would otherwise wait out every one.
/// </summary>
[TestFixture]
public class HeadlessWindowWakeTest
{
    [Test]
    [FlakyTest]
    public void AWakeFromAnotherThreadEndsTheWait()
    {
        IWindow window = new HeadlessWindow();

        var waker = new Thread(() =>
        {
            Thread.Sleep(50);
            window.WakeEventThread();
        });

        var waited = Stopwatch.StartNew();
        waker.Start();
        window.WaitForEvents(5000);
        waker.Join();

        Assert.That(waited.ElapsedMilliseconds, Is.LessThan(1000));
    }

    [Test]
    public void AWakeBeforeTheWaitIsNotLost()
    {
        IWindow window = new HeadlessWindow();

        window.WakeEventThread();

        var waited = Stopwatch.StartNew();
        window.WaitForEvents(5000);

        Assert.That(waited.ElapsedMilliseconds, Is.LessThan(1000));
    }

    [Test]
    public void AWakeEndsOnlyOneWait()
    {
        IWindow window = new HeadlessWindow();

        window.WakeEventThread();
        window.WakeEventThread();
        window.WaitForEvents(5000);

        var waited = Stopwatch.StartNew();
        window.WaitForEvents(50);

        Assert.That(waited.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(45));
    }
}
