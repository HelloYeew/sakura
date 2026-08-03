// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using BenchmarkDotNet.Attributes;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Benchmarks.Benchmarks;

/// <summary>
/// Measures screen-teardown cost. Removing a subtree used to be free beyond detaching it, it now walks
/// the subtree and disposes every drawable in it, which is the one path the disposal cascade made
/// materially more expensive.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(invocationCount: 1)]
public class DisposalBenchmarks
{
    public enum TreeShape
    {
        /// <summary>1 container × 1000 boxes. The flat case: 1001 disposals, no depth.</summary>
        Wide1000,

        /// <summary>10 containers × 100 boxes. Closest to a real screen.</summary>
        Grid10X100,

        /// <summary>100 nested containers with one leaf box. Worst case for a breadth-first drain.</summary>
        Deep100,
    }

    [Params(TreeShape.Wide1000, TreeShape.Grid10X100, TreeShape.Deep100)]
    public TreeShape Shape;

    private Container root = null!;
    private ManualClock clock = null!;
    private Container subtree = null!;

    [IterationSetup]
    public void SetupIteration()
    {
        // Disposal is inline while building so nothing from a previous iteration can be left queued
        // against a Process call this one is timing.
        DrawableDisposalQueue.Enabled = false;
        DrawableDisposalQueue.Flush();

        (root, clock) = BenchmarkTree.CreateRoot();

        switch (Shape)
        {
            case TreeShape.Wide1000:
                subtree = BenchmarkTree.AddWide(root, 1000);
                break;

            case TreeShape.Grid10X100:
                subtree = BenchmarkTree.AddGrid(root, 10, 100);
                break;

            case TreeShape.Deep100:
                (subtree, _) = BenchmarkTree.AddDeep(root, 100);
                break;
        }

        BenchmarkTree.LoadAndSettle(root, clock);
    }

    [IterationCleanup]
    public void CleanupIteration()
    {
        DrawableDisposalQueue.Flush();
        DrawableDisposalQueue.Enabled = false;
    }

    /// <summary>
    /// The pre-cascade baseline: detach only. Everything the other two cost above this is new work.
    /// Also the cost of a reparenting removal, which is the only case that still opts out.
    /// </summary>
    [Benchmark(Baseline = true)]
    public void RemoveSubtreeWithoutDisposal() => root.Remove(subtree, false);

    /// <summary>
    /// The disposal work with no queue in the way: one recursive walk, all in this call.
    /// </summary>
    [Benchmark]
    public void RemoveSubtreeInline()
    {
        DrawableDisposalQueue.Enabled = false;
        root.Remove(subtree);
    }

    /// <summary>
    /// The same total work routed through the queue and drained in full. The difference against
    /// <see cref="RemoveSubtreeInline"/> is the queue's overhead per drawable; in a running app that
    /// difference is also spread over several frames instead of landing in one.
    /// </summary>
    [Benchmark]
    public void RemoveSubtreeQueued()
    {
        DrawableDisposalQueue.Enabled = true;
        root.Remove(subtree);
        DrawableDisposalQueue.Flush();
    }

    /// <summary>
    /// <c>Clear</c> rather than <c>Remove</c>: the same cascade entered once per child instead of once
    /// for the whole subtree, which is what a container rebuilding its contents pays.
    /// </summary>
    [Benchmark]
    public void ClearChildrenInline()
    {
        DrawableDisposalQueue.Enabled = false;
        subtree.Clear();
    }

    [Benchmark]
    public void ClearChildrenWithoutDisposal() => subtree.Clear(false);
}
