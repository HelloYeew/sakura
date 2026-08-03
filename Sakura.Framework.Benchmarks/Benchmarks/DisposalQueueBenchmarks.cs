// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Benchmarks.Benchmarks;

/// <summary>
/// Isolates <see cref="DrawableDisposalQueue"/> itself: what one frame's drain costs at the shipped
/// budget, and how enqueue scales with the number of drawables in flight.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(invocationCount: 1)]
public class DisposalQueueBenchmarks
{
    [Params(250, 1000, 5000)]
    public int Items;

    private readonly List<Drawable> pending = new List<Drawable>();

    [IterationSetup]
    public void SetupIteration()
    {
        DrawableDisposalQueue.Enabled = true;
        DrawableDisposalQueue.ItemsPerFrameBudget = DrawableDisposalQueue.DEFAULT_ITEMS_PER_FRAME;
        DrawableDisposalQueue.Flush();

        pending.Clear();

        for (int i = 0; i < Items; i++)
            pending.Add(new Box { Size = new Vector2(16) });
    }

    [IterationCleanup]
    public void CleanupIteration()
    {
        DrawableDisposalQueue.Flush();
        DrawableDisposalQueue.Enabled = false;
    }

    /// <summary>
    /// Enqueue only, nothing drained.
    /// </summary>
    [Benchmark(Baseline = true)]
    public void Enqueue()
    {
        for (int i = 0; i < pending.Count; i++)
            DrawableDisposalQueue.Enqueue(pending[i]);
    }

    /// <summary>
    /// One frame's worth of draining at the shipped budget. This is the figure that has to stay well
    /// inside a frame at 120 Hz, and it is the per-frame cost of a teardown *regardless* of how large the
    /// removed subtree was — anything past the budget carries to the next frame. It is also how to judge
    /// whether 250 is the right budget, which was reasoned about rather than measured.
    /// </summary>
    [Benchmark]
    public void EnqueueAndProcessOneFrame()
    {
        for (int i = 0; i < pending.Count; i++)
            DrawableDisposalQueue.Enqueue(pending[i]);

        DrawableDisposalQueue.Process();
    }

    /// <summary>
    /// Draining everything queued, for the total that the budgeted figure above is a slice of.
    /// </summary>
    [Benchmark]
    public void EnqueueAndFlushAll()
    {
        for (int i = 0; i < pending.Count; i++)
            DrawableDisposalQueue.Enqueue(pending[i]);

        DrawableDisposalQueue.Flush();
    }
}
