// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Graphics.Performance;

namespace Sakura.Framework.Tests.Graphics;

[TestFixture]
public class AudioMeterBallisticsTest
{
    // an unlocked 240 Hz target against a vsynced 60 Hz
    private const double fast_frame_ms = 1000d / 240;
    private const double slow_frame_ms = 1000d / 60;

    [Test]
    public void TestRiseIsFrameRateIndependent()
    {
        var fast = run(fast_frame_ms, 500, 1f, 0f);
        var slow = run(slow_frame_ms, 500, 1f, 0f);

        Assert.That(fast.Level, Is.EqualTo(slow.Level).Within(0.005f));

        // A rise that did not actually get anywhere would pass the comparison above trivially.
        Assert.That(fast.Level, Is.GreaterThan(0.9f));
    }

    [Test]
    public void TestFallIsFrameRateIndependent()
    {
        var fast = new AudioMeterBallistics();
        var slow = new AudioMeterBallistics();

        advance(fast, fast_frame_ms, 500, 1f, 0f);
        advance(slow, slow_frame_ms, 500, 1f, 0f);

        advance(fast, fast_frame_ms, 400, 0f, 0f);
        advance(slow, slow_frame_ms, 400, 0f, 0f);

        Assert.That(fast.Level, Is.EqualTo(slow.Level).Within(0.005f));

        // Somewhere in the middle of the fall, where a per-frame decay and a per-second one differ most.
        Assert.That(fast.Level, Is.GreaterThan(0.05f).And.LessThan(0.5f));
    }

    /// <summary>
    /// The marker fell by a fixed step per frame before, so at 240 Hz it emptied four times faster
    /// than at 60 Hz — the reading that made two runs of the same app incomparable.
    /// </summary>
    [Test]
    public void TestPeakDecayIsFrameRateIndependent()
    {
        var fast = new AudioMeterBallistics();
        var slow = new AudioMeterBallistics();

        advance(fast, fast_frame_ms, 100, 1f, 0f);
        advance(slow, slow_frame_ms, 100, 1f, 0f);

        Assert.That(fast.Peak, Is.EqualTo(1f).Within(0.001f));
        Assert.That(slow.Peak, Is.EqualTo(1f).Within(0.001f));

        // Past the hold, and far enough into the fall to tell a per-second decay from a per-frame one.
        advance(fast, fast_frame_ms, 2000, 0f, 0f);
        advance(slow, slow_frame_ms, 2000, 0f, 0f);

        Assert.That(fast.Peak, Is.EqualTo(slow.Peak).Within(0.005f));
        Assert.That(fast.Peak, Is.GreaterThan(0f).And.LessThan(1f));
    }

    [Test]
    public void TestPeakHoldsBeforeFalling()
    {
        var meter = new AudioMeterBallistics();

        advance(meter, slow_frame_ms, 100, 1f, 0f);

        // inside the hold window, so nothing should have moved yet.
        advance(meter, slow_frame_ms, AudioMeterBallistics.PEAK_HOLD_MS * 0.5, 0f, 0f);
        Assert.That(meter.Peak, Is.EqualTo(1f).Within(0.001f));

        advance(meter, slow_frame_ms, AudioMeterBallistics.PEAK_HOLD_MS, 0f, 0f);
        Assert.That(meter.Peak, Is.LessThan(1f));
    }

    [Test]
    public void TestClipLatchesForItsHoldAndThenClears()
    {
        var meter = new AudioMeterBallistics();

        // One frame at full scale, which is all a transient gives you.
        meter.Advance(1f, 1f, slow_frame_ms);
        Assert.That(meter.Clipped, Is.True);

        advance(meter, slow_frame_ms, AudioMeterBallistics.CLIP_HOLD_MS * 0.5, 0f, 0f);
        Assert.That(meter.Clipped, Is.True, "the latch let go inside its hold window");

        advance(meter, slow_frame_ms, AudioMeterBallistics.CLIP_HOLD_MS, 0f, 0f);
        Assert.That(meter.Clipped, Is.False, "the latch never expired");
    }

    /// <summary>
    /// A signal must not be reported as clipping just because it reached the top of the meter's dB
    /// scale: the latch is fed the raw amplitude, not the bar's position.
    /// </summary>
    [Test]
    public void TestFullScaleBarDoesNotClipOnItsOwn()
    {
        var meter = new AudioMeterBallistics();

        advance(meter, slow_frame_ms, 500, 1f, 0.5f);

        Assert.That(meter.Level, Is.GreaterThan(0.9f));
        Assert.That(meter.Clipped, Is.False);
    }

    /// <summary>
    /// The window can be opened after the app has been stalled, and the first frame then reports the
    /// whole stall. Advancing by all of it would snap the bar to wherever the signal happens to be.
    /// </summary>
    [Test]
    public void TestOneEnormousStepDoesNotSnap()
    {
        var meter = new AudioMeterBallistics();

        meter.Advance(1f, 0f, 5000);

        Assert.That(meter.Level, Is.LessThan(1f));
    }

    [Test]
    public void TestZeroElapsedTimeMovesNothing()
    {
        var meter = new AudioMeterBallistics();

        meter.Advance(1f, 0f, 0);

        Assert.That(meter.Level, Is.EqualTo(0f).Within(0.0001f));
    }

    private static AudioMeterBallistics run(double frameMs, double durationMs, float target, float amplitude)
    {
        var meter = new AudioMeterBallistics();
        advance(meter, frameMs, durationMs, target, amplitude);
        return meter;
    }

    /// <summary>
    /// Advances a meter over <paramref name="durationMs"/> of wall-clock time in steps of
    /// <paramref name="frameMs"/>, so two rates can be compared over the same amount of time rather
    /// than the same number of frames — which is the whole distinction being scored.
    /// </summary>
    private static void advance(AudioMeterBallistics meter, double frameMs, double durationMs, float target, float amplitude)
    {
        int frames = (int)Math.Round(durationMs / frameMs);

        for (int i = 0; i < frames; i++)
            meter.Advance(target, amplitude, frameMs);
    }
}
