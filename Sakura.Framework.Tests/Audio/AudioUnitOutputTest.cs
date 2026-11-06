// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Diagnostics;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Sakura.Framework.Audio.SdlEngine;

namespace Sakura.Framework.Tests.Audio;

/// <summary>
/// The HAL output on this Mac's own devices, playing a silent engine: it plays, on the device it's given, at the I/O
/// buffer asked for, and keeps playing as it's moved onto another device.
/// </summary>
[TestFixture]
public class AudioUnitOutputTest
{
    private const int sample_rate = 48000;

    private uint defaultDevice;

    [SetUp]
    public void SetUp()
    {
        if (!RuntimeInfo.IsMacOS)
            Assert.Ignore("The HAL output is macOS's; RemoteIO is checked on the iPhone.");

        if (!AudioUnitOutput.IsAvailable)
            Assert.Ignore("libsakura-audio has no audio unit callback here.");

        defaultDevice = CoreAudioNative.DefaultOutputDevice();

        if (defaultDevice == 0)
            Assert.Ignore("This Mac has no output device.");
    }

    [Test]
    public void ItPlaysOnTheDeviceItIsGivenAtTheBufferAskedFor()
    {
        using var engine = SakuraAudioEngine.Create(sample_rate, 2)!;
        using var output = AudioUnitOutput.TryOpen(engine, defaultDevice, bufferFrames: 256);

        Assert.That(output, Is.Not.Null);
        Assert.That(output!.Start(), Is.True);

        assertRenders(engine, "on the default");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(output.PlaysOnADevice, Is.True);
            Assert.That(output.Device, Is.EqualTo(defaultDevice));
            Assert.That(output.DeviceName, Is.EqualTo(CoreAudioNative.GetDeviceName(defaultDevice)));

            // In the device's own frames, rounded there and back.
            Assert.That(output.BufferFrames, Is.EqualTo(256).Within(2), "the I/O buffer asked for");
            Assert.That(output.TrySampleLatency(out double latency), Is.True);
            Assert.That(latency, Is.InRange(0, 100));
        }
    }

    [Test]
    public void ItKeepsPlayingAsItMovesOntoAnotherDevice()
    {
        uint other = CoreAudioNative.GetObjects(CoreAudioNative.SYSTEM_OBJECT, CoreAudioNative.PROPERTY_DEVICES, CoreAudioNative.SCOPE_GLOBAL)
                                    .FirstOrDefault(d => d != defaultDevice
                                                         && CoreAudioNative.GetObjects(d, CoreAudioNative.PROPERTY_STREAMS, CoreAudioNative.SCOPE_OUTPUT).Length > 0);

        if (other == 0)
            Assert.Ignore("This Mac has one output device, so there's nowhere to move to.");

        using var engine = SakuraAudioEngine.Create(sample_rate, 2)!;
        using var output = AudioUnitOutput.TryOpen(engine, defaultDevice);

        Assert.That(output, Is.Not.Null);
        output!.Start();
        assertRenders(engine, "on the default");

        Assert.That(output.PlayOn(other), Is.True, $"onto {CoreAudioNative.GetDeviceName(other)}");
        Assert.That(output.Device, Is.EqualTo(other));
        assertRenders(engine, "on the other device");

        Assert.That(output.PlayOn(defaultDevice), Is.True, "back onto the default");
        assertRenders(engine, "back on the default");
    }

    /// <summary>
    /// Waits for the unit to render the engine again.
    /// </summary>
    private static void assertRenders(SakuraAudioEngine engine, string where)
    {
        long before = engine.GetStats().Callbacks;
        var waited = Stopwatch.StartNew();

        while (engine.GetStats().Callbacks <= before + 2 && waited.ElapsedMilliseconds < 3000)
            Thread.Sleep(10);

        Assert.That(engine.GetStats().Callbacks, Is.GreaterThan(before + 2), $"the unit stopped rendering {where}");
    }
}
