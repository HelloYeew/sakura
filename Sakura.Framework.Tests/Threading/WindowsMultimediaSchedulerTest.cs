// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Runtime.Versioning;
using NUnit.Framework;
using Sakura.Framework.Threading;

namespace Sakura.Framework.Tests.Threading;

public class WindowsMultimediaSchedulerTest
{
    /// <summary>
    /// Mainly checks the avrt.dll signatures, which nothing else exercises unless
    /// <see cref="Sakura.Framework.Platform.HostOptions.UseMultimediaClassScheduler"/> is turned on.
    /// </summary>
    [Test]
    [Platform("Win")]
    [SupportedOSPlatform("windows")]
    public void TestRegisterAndRevert()
    {
        using var registration = WindowsMultimediaScheduler.Register(nameof(TestRegisterAndRevert));

        Assume.That(registration, Is.Not.Null, "MMCSS is unavailable on this machine");
    }
}
