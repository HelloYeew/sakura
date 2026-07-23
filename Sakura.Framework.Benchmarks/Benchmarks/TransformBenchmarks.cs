// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using BenchmarkDotNet.Attributes;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Benchmarks.Benchmarks;

/// <summary>
/// Measures the transform (animation) system: scheduling cost, per-frame application cost
/// with many active transforms, and the bookkeeping queries used by Expire/Loop.
/// </summary>
[MemoryDiagnoser]
public class TransformBenchmarks
{
    private const int box_count = 256;

    private Container root = null!;
    private ManualClock clock = null!;
    private Container holder = null!;
    private Box scheduleTarget = null!;

    [GlobalSetup]
    public void Setup()
    {
        (root, clock) = BenchmarkTree.CreateRoot();
        holder = BenchmarkTree.AddWide(root, box_count);
        scheduleTarget = new Box { Size = new Vector2(16, 16) };
        root.Add(scheduleTarget);
        BenchmarkTree.LoadAndSettle(root, clock);

        // Give every box two effectively-infinite transforms so they stay active
        // for the whole measurement (movement + fade, the typical note state).
        foreach (var child in holder.Children)
        {
            child.MoveTo(new Vector2(640, 720), 1e12);
            child.FadeTo(0.5f, 1e12);
        }

        // One settle frame so the transforms' start times are in the past.
        clock.CurrentTime += BenchmarkTree.FRAME_STEP_MS;
        root.UpdateSubTree();
    }

    /// <summary>
    /// One update frame with 256 boxes × 2 active transforms being applied.
    /// This is the steady-state cost of animating everything via the transform system.
    /// </summary>
    [Benchmark(Baseline = true)]
    public void ApplyActiveTransforms_256Boxes()
    {
        clock.CurrentTime += BenchmarkTree.FRAME_STEP_MS;
        root.UpdateSubTree();
    }

    /// <summary>
    /// Scheduling cost check by queue 10 transforms on one drawable, then clear them.
    /// Tracks allocation per scheduled transform. Note: with retarget-in-place the consecutive
    /// same-member immediate calls here (three <c>MoveTo*</c> → Position, three fades → Alpha) redirect
    /// the in-flight transform instead of accumulating, so the drawable holds ~5 transforms rather than
    /// 10 - the transient objects are still built by the fluent helpers, so allocation is unchanged.
    /// </summary>
    [Benchmark]
    public void Schedule10Transforms_ThenClear()
    {
        scheduleTarget.MoveTo(new Vector2(100, 100), 1000)
                      .FadeTo(0f, 1000);
        scheduleTarget.MoveToX(50, 500);
        scheduleTarget.MoveToY(50, 500);
        scheduleTarget.ScaleTo(2f, 500);
        scheduleTarget.RotateTo(90, 500);
        scheduleTarget.FadeIn(200);
        scheduleTarget.ResizeTo(new Vector2(32, 32), 300);
        scheduleTarget.MoveTo(new Vector2(0, 0), 100);
        scheduleTarget.FadeOut(100);

        scheduleTarget.ClearTransforms();
    }

    /// <summary>
    /// Replicate SliderBar: one property retargeted every frame. With retarget-in-place each subsequent
    /// <c>ResizeTo</c> redirects the single in-flight transform rather than clearing and re-adding,
    /// so the transform list stays bounded at one. Tracks the per-retarget cost (the same-member scan
    /// plus the transient transform the fluent helper builds), which is the real cost of animating a
    /// slider fill / scrubber that follows continuous input.
    /// </summary>
    [Benchmark]
    public void RetargetChurn_SliderDrag()
    {
        // First call has nothing in flight and adds; the rest redirect it in place. A long duration
        // keeps it in flight for the whole churn so every call after the first hits the retarget path.
        for (int i = 0; i < 60; i++)
            scheduleTarget.ResizeTo(new Vector2(16 + i % 40, 16), 1_000);

        scheduleTarget.ClearTransforms();
    }

    /// <summary>
    /// The query used by <c>Expire</c>;
    /// </summary>
    [Benchmark]
    public double GetLatestTransformEndTime()
    {
        // Boxes in the holder each hold 2 long-running transforms.
        return holder.Children[0].GetLatestTransformEndTime();
    }
}
