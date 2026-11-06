// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Audio;
using Sakura.Framework.Audio.SdlEngine;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Audio;

/// <summary>
/// How each of the SDL backend's outputs splits its latency: where the app's own buffers end and the system's part
/// starts, and how each part is known. AAudio and RemoteIO only play on a phone, so they're split here from figures. The
/// HAL output on macOS splits the way RemoteIO does.
/// </summary>
[TestFixture]
public class OutputLatencyPartsTest
{
    [Test]
    public void ThroughSDLWithoutARouteNothingReportsTheSystemsPart()
    {
        var parts = SDLAudioManager.SDLLatencyParts(queueMs: 0.5, deviceMs: 32, routeMs: null, routeReachesListener: true);

        Assert.That(parts, Is.EqualTo(new[]
        {
            new AudioLatencyPart(AudioLatencyPartKind.Queue, 0.5, LatencySource.Counted),
            new AudioLatencyPart(AudioLatencyPartKind.AppBuffer, 32, LatencySource.FromBufferSizes),
            AudioLatencyPart.NotReported(AudioLatencyPartKind.System)
        }));
        assertAddsUpTo(parts, 32.5);
    }

    [Test]
    public void ThroughSDLTheRouteIsTheSystemsPart()
    {
        var parts = SDLAudioManager.SDLLatencyParts(queueMs: 0, deviceMs: 32, routeMs: 7.4, routeReachesListener: true);

        Assert.That(parts[^1], Is.EqualTo(new AudioLatencyPart(AudioLatencyPartKind.System, 7.4, LatencySource.ReportedBySystem)));
        assertAddsUpTo(parts, 39.4);
    }

    [Test]
    public void ThroughSDLARouteThatStopsAtTheDeviceLeavesTheRestUnreported()
    {
        var parts = SDLAudioManager.SDLLatencyParts(queueMs: 0, deviceMs: 15, routeMs: 10, routeReachesListener: false);

        Assert.That(parts.Skip(2), Is.EqualTo(new[]
        {
            new AudioLatencyPart(AudioLatencyPartKind.System, 10, LatencySource.ReportedBySystem),
            AudioLatencyPart.NotReported(AudioLatencyPartKind.AfterDevice)
        }));
        assertAddsUpTo(parts, 25);
    }

    [Test]
    public void ThroughAAudioBeforeTheTimestampsOnlyTheBufferIsKnown()
    {
        var parts = SDLAudioManager.AAudioLatencyParts(deviceMs: 4, bufferMs: 4, measured: false);

        Assert.That(parts, Is.EqualTo(new[]
        {
            new AudioLatencyPart(AudioLatencyPartKind.AppBuffer, 4, LatencySource.FromBufferSizes),
            AudioLatencyPart.NotReported(AudioLatencyPartKind.System)
        }));
        assertAddsUpTo(parts, 4);
    }

    [Test]
    public void ThroughAAudioTheSystemsPartIsTheMeasuredFigureLessTheBuffer()
    {
        var parts = SDLAudioManager.AAudioLatencyParts(deviceMs: 30, bufferMs: 4, measured: true);

        Assert.That(parts, Is.EqualTo(new[]
        {
            new AudioLatencyPart(AudioLatencyPartKind.AppBuffer, 4, LatencySource.FromBufferSizes),
            new AudioLatencyPart(AudioLatencyPartKind.System, 26, LatencySource.Measured)
        }));
        assertAddsUpTo(parts, 30);
    }

    [Test]
    public void ThroughAAudioAMeasurementShorterThanTheBufferLeavesTheSystemNothing()
    {
        var parts = SDLAudioManager.AAudioLatencyParts(deviceMs: 3, bufferMs: 4, measured: true);

        Assert.That(parts[0].Milliseconds, Is.EqualTo(3));
        Assert.That(parts[1].Milliseconds, Is.Zero);
        assertAddsUpTo(parts, 3);
    }

    [Test]
    public void ThroughAnAudioUnitItIsAllTheSystemsPart()
    {
        var parts = SDLAudioManager.AudioUnitLatencyParts(deviceMs: 10, routeMs: 15, measured: true);

        Assert.That(parts, Is.EqualTo(new[]
        {
            new AudioLatencyPart(AudioLatencyPartKind.System, 25, LatencySource.Measured | LatencySource.ReportedBySystem)
        }));
    }

    [Test]
    public void ThroughAnAudioUnitBeforeTheTimestampsTheSystemReportsIt()
    {
        var parts = SDLAudioManager.AudioUnitLatencyParts(deviceMs: 0, routeMs: 20, measured: false);

        Assert.That(parts, Is.EqualTo(new[]
        {
            new AudioLatencyPart(AudioLatencyPartKind.System, 20, LatencySource.ReportedBySystem)
        }));
    }

    [Test]
    public void ThroughAnAudioUnitWithoutARouteNothingReportsWhatComesAfterTheDevice()
    {
        var parts = SDLAudioManager.AudioUnitLatencyParts(deviceMs: 5.3, routeMs: null, measured: false);

        Assert.That(parts, Is.EqualTo(new[]
        {
            new AudioLatencyPart(AudioLatencyPartKind.System, 5.3, LatencySource.FromBufferSizes),
            AudioLatencyPart.NotReported(AudioLatencyPartKind.AfterDevice)
        }));
        assertAddsUpTo(parts, 5.3);
    }

    /// <summary>
    /// The parts add up to the whole, and a part has a figure exactly when something reports it.
    /// </summary>
    private static void assertAddsUpTo(IReadOnlyList<AudioLatencyPart> parts, double totalMs)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(parts.Sum(p => p.Milliseconds ?? 0), Is.EqualTo(totalMs).Within(1e-9));

            foreach (var part in parts)
                Assert.That(part.Milliseconds.HasValue, Is.EqualTo(part.Source != LatencySource.NotReported), $"{part.Kind}");
        }
    }
}
