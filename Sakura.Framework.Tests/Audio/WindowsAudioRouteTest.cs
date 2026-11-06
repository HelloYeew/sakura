// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Audio.SdlEngine;

namespace Sakura.Framework.Tests.Audio;

/// <summary>
/// Reads this machine's own output endpoints through <see cref="WindowsAudioRoute"/>, the way SDL's output counts the
/// shared engine after its buffers. On a machine with no output, only that the endpoints can be listed.
/// </summary>
[TestFixture]
public class WindowsAudioRouteTest
{
    private string? defaultName;

    [SetUp]
    public void SetUp()
    {
        if (!RuntimeInfo.IsWindows)
            Assert.Ignore("Windows only.");

        nint enumerator = WasapiNative.CreateEnumerator();
        Assert.That(enumerator, Is.Not.Zero, "every Windows has the endpoint enumerator");

        try
        {
            // Lists the endpoints whatever there is, so the method tables are called even with no output.
            Assert.That(WasapiNative.FindOutput(enumerator, "Not an endpoint on any machine"), Is.Zero);

            nint device = WasapiNative.GetDefaultOutput(enumerator);

            if (device == 0)
                Assert.Ignore("This machine has no output.");

            defaultName = WasapiNative.GetName(device);
            WasapiNative.Release(device);
        }
        finally
        {
            WasapiNative.Release(enumerator);
        }
    }

    [Test]
    public void TheDefaultOutputCountsTheEnginesPeriod()
    {
        var route = new WindowsAudioRoute();

        Assert.Multiple(() =>
        {
            Assert.That(route.Name, Is.Empty, "the output keeps SDL's name");
            Assert.That(route.ReachesListener, Is.False, "nothing reports the driver and the device");

            // From 2 ms (a driver that supports low latency) to 10 ms (the usual default) and a little over.
            Assert.That(route.LatencyMilliseconds, Is.InRange(1, 30));
        });
    }

    [Test]
    public void AnOutputNamedAsSDLNamesItReadsTheSame()
    {
        Assert.That(defaultName, Is.Not.Null.And.Not.Empty, "the default output's friendly name");

        var onDefault = new WindowsAudioRoute();
        var named = new WindowsAudioRoute();
        named.SetPlayingDevice(defaultName!);

        Assert.That(named.LatencyMilliseconds, Is.EqualTo(onDefault.LatencyMilliseconds).Within(0.001));
    }

    [Test]
    public void AnOutputThatIsNotThereCountsNothing()
    {
        var route = new WindowsAudioRoute();
        route.SetPlayingDevice("Not an endpoint on any machine");

        Assert.That(route.LatencyMilliseconds, Is.Zero);
    }
}
