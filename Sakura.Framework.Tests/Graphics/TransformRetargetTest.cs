// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Extensions.TransformSequenceExtensions;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// Covers retarget-in-place: an immediate transform on a property that is already animating redirects
/// the in-flight transform instead of restarting it. This is what stops a per-frame retarget (e.g. a
/// slider fill tracking a drag) from freezing at progress 0 when the update rate is low.
/// </summary>
[TestFixture]
public class TransformRetargetTest
{
    private ManualClock manual = null!;
    private FramedClock rootClock = null!;
    private Container root = null!;
    private Box box = null!;

    [OneTimeSetUp]
    public void InitializeLogger() => Logger.Initialize();

    [OneTimeTearDown]
    public void ShutdownLogger() => Logger.Shutdown();

    [SetUp]
    public void SetUp()
    {
        manual = new ManualClock
        {
            CurrentTime = 1000
        };
        rootClock = new FramedClock(manual);
        root = new Container
        {
            Size = new Vector2(800, 600),
            Clock = rootClock
        };
        box = new Box
        {
            Size = new Vector2(0, 20)
        };
        root.Add(box);

        root.Load();
        root.LoadComplete();

        for (int i = 0; i < 3; i++)
            frame(null);
    }

    /// <summary>
    /// Advances one frame faithfully to the real app order: the update clock is advanced "before" the
    /// per-frame action (which stands in for input dispatch), so a transform created in the action and
    /// the transforms applied in <see cref="Drawable.UpdateSubTree"/> share the same frame clock value.
    /// </summary>
    private void frame(Action? atFrameStart, double advance = 16)
    {
        manual.CurrentTime += advance;
        rootClock.ProcessFrame();
        atFrameStart?.Invoke();
        root.UpdateSubTree();
    }

    private static List<Transform> transformsOf(Drawable d)
    {
        var field = typeof(Drawable).GetField("transforms", BindingFlags.NonPublic | BindingFlags.Instance);
        var list = (IEnumerable?)field!.GetValue(d);
        var result = new List<Transform>();
        if (list != null)
            result.AddRange(list.Cast<Transform>());
        return result;
    }

    [Test]
    public void TestPerFrameRetargetDoesNotFreeze()
    {
        // Change the resize target every frame (a drag under a low/stuttering update rate). The fill
        // must climb toward the target rather than freeze at 0.
        float[] widths = new float[10];
        for (int i = 0; i < 10; i++)
        {
            float target = (i + 1) / 10f * 200f;
            frame(() => box.ResizeTo(new Vector2(target, 20), 150));
            widths[i] = box.Size.X;
        }

        using (Assert.EnterMultipleScope())
        {
            for (int i = 1; i < widths.Length; i++)
                Assert.That(widths[i], Is.GreaterThanOrEqualTo(widths[i - 1] - 0.01f), $"regressed at frame {i}: {string.Join(",", widths)}");

            Assert.That(widths[^1], Is.GreaterThan(40f), $"fill froze: {string.Join(",", widths)}");
        }
    }

    [Test]
    public void TestInFlightRetargetPreservesTimeline()
    {
        frame(() => box.ResizeTo(new Vector2(200, 20), 200));

        // partway through the first animation
        frame(null, 80);
        float mid = box.Size.X;
        Assert.That(mid, Is.GreaterThan(0f).And.LessThan(200f), "should be mid-animation");

        // retarget to a smaller value; must continue from the current value, not snap to 0 or restart
        frame(() => box.ResizeTo(new Vector2(120, 20), 200));
        Assert.That(box.Size.X, Is.GreaterThan(0f), "must not snap back to start on retarget");

        // let it finish and settle exactly on the latest target
        for (int i = 0; i < 20; i++) frame(null, 20);
        Assert.That(box.Size.X, Is.EqualTo(120f).Within(0.01f), "must settle on the latest target");
    }

    [Test]
    public void TestOnlyOneInFlightTransformAccumulates()
    {
        for (int i = 0; i < 10; i++)
        {
            float target = (i + 1) / 10f * 200f;
            frame(() => box.ResizeTo(new Vector2(target, 20), 150));
        }

        int sizeTransforms = transformsOf(box).Count(t => t.Member == TransformMember.Size);

        Assert.That(sizeTransforms, Is.LessThanOrEqualTo(1), "per-frame retargets must not accumulate transforms");
    }

    [Test]
    public void TestInterruptIsSmooth()
    {
        box.Alpha = 0;
        frame(() => box.FadeTo(1f, 300));

        frame(null, 150); // ~ halfway up
        float mid = box.Alpha;
        Assert.That(mid, Is.GreaterThan(0.2f).And.LessThan(1f), "fade-in should be partway");

        // interrupt with an immediate fade-out: alpha must continue DOWN from the current value,
        // not jump to 0 (a fresh transform would capture start=current, but the timeline is preserved
        // so it eases from mid toward 0).
        frame(() => box.FadeTo(0f, 300));
        Assert.That(box.Alpha, Is.LessThanOrEqualTo(mid + 0.01f), "interrupt must not jump upward");

        for (int i = 0; i < 30; i++) frame(null, 20);
        Assert.That(box.Alpha, Is.EqualTo(0f).Within(0.01f), "interrupt must settle on the new target");
    }

    [Test]
    public void TestChainingPreserved()
    {
        // FadeIn then (sequentially) FadeOut. The delayed FadeOut must NOT retarget the FadeIn; both
        // legs must run.
        box.Alpha = 0;
        frame(() => box.TransformSequence().FadeTo(1f, 200).Then().FadeTo(0f, 200));

        // during the first leg alpha rises
        for (int i = 0; i < 8; i++) frame(null, 20);
        float peak = box.Alpha;
        Assert.That(peak, Is.GreaterThan(0.5f), "first leg (fade-in) must run");

        // during the second leg alpha falls back down
        for (int i = 0; i < 15; i++) frame(null, 20);
        Assert.That(box.Alpha, Is.LessThan(0.2f), "second leg (fade-out) must run after the first");
    }

    [Test]
    public void TestLoopingTransformNotRetargeted()
    {
        // a looping spin (Rotation, looping) plus an immediate RotateTo must coexist: the immediate one
        // must not retarget/absorb the loop. (Spin uses member None, RotateTo uses Rotation, so this
        // also verifies members don't accidentally collide.)
        box.Spin(1000);
        frame(() => box.RotateTo(45f, 100));

        int looping = 0;
        foreach (var t in transformsOf(box))
            if (t.IsLooping)
                looping++;

        Assert.That(looping, Is.GreaterThanOrEqualTo(1), "the looping transform must survive an immediate same-axis add");
    }
}
