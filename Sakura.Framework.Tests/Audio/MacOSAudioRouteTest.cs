// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Diagnostics.CodeAnalysis;
using NUnit.Framework;
using Sakura.Framework.Audio.SdlEngine;
using static Sakura.Framework.Audio.SdlEngine.CoreAudioNative;

namespace Sakura.Framework.Tests.Audio;

/// <summary>
/// Reads this Mac's own output devices through <see cref="MacOSAudioRoute"/>, the way SDL's output counts what comes
/// after its buffers.
/// </summary>
[TestFixture]
[SuppressMessage("ReSharper", "InconsistentNaming")]
public class MacOSAudioRouteTest
{
    private uint defaultDevice;

    [SetUp]
    public void SetUp()
    {
        if (!RuntimeInfo.IsMacOS)
            Assert.Ignore("macOS only.");

        if (!TryGet(SYSTEM_OBJECT, PROPERTY_DEFAULT_OUTPUT_DEVICE, SCOPE_GLOBAL, out defaultDevice) || defaultDevice == 0)
            Assert.Ignore("This Mac has no output device.");
    }

    [Test]
    public void TheDefaultOutputCountsItsBufferAndWhatComesAfter()
    {
        var route = new MacOSAudioRoute();

        Assert.Multiple(() =>
        {
            Assert.That(route.Name, Is.Empty, "the output keeps SDL's name");
            Assert.That(route.BufferMilliseconds, Is.GreaterThan(0), "every device has an I/O buffer");
            Assert.That(route.LatencyMilliseconds, Is.GreaterThanOrEqualTo(route.BufferMilliseconds));
        });
    }

    [Test]
    public void ADeviceNamedAsSDLNamesItReadsTheSame()
    {
        string? name = GetString(defaultDevice, PROPERTY_NAME, SCOPE_OUTPUT)?.TrimEnd(' ');
        Assert.That(name, Is.Not.Null.And.Not.Empty, "Core Audio's name for the default output");

        var onDefault = new MacOSAudioRoute();
        var named = new MacOSAudioRoute();
        named.SetPlayingDevice(name!);

        Assert.That(named.LatencyMilliseconds, Is.EqualTo(onDefault.LatencyMilliseconds).Within(0.001));
    }

    [Test]
    public void ADeviceThatIsNotThereCountsNothing()
    {
        var route = new MacOSAudioRoute();
        route.SetPlayingDevice("Not a device on any Mac");

        Assert.Multiple(() =>
        {
            Assert.That(route.LatencyMilliseconds, Is.Zero);
            Assert.That(route.BufferMilliseconds, Is.Zero);
        });

        route.SetPlayingDevice(string.Empty);
        Assert.That(route.LatencyMilliseconds, Is.GreaterThan(0), "back on the default");
    }
}
