// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using BenchmarkDotNet.Attributes;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Benchmarks.Benchmarks;

/// <summary>
/// Isolates the invalidation mechanics around property sets: the guard path (no change),
/// the repeat-invalidation early exit, and the full cost of one leaf change propagating
/// through a deep tree and being cleaned up by an update pass.
/// </summary>
[MemoryDiagnoser]
public class InvalidationBenchmarks
{
    private Container deepRoot = null!;
    private ManualClock deepClock = null!;
    private Box deepLeaf = null!;

    private Container dirtyRoot = null!;
    private Box dirtyLeaf = null!;

    private int frame;

    [GlobalSetup]
    public void Setup()
    {
        (deepRoot, deepClock) = BenchmarkTree.CreateRoot();
        (_, deepLeaf) = BenchmarkTree.AddDeep(deepRoot, 100);
        BenchmarkTree.LoadAndSettle(deepRoot, deepClock);

        // A second tree we deliberately keep dirty (never updated after setup)
        // to measure the repeated-invalidation early-exit path.
        (dirtyRoot, _) = BenchmarkTree.CreateRoot();
        (_, dirtyLeaf) = BenchmarkTree.AddDeep(dirtyRoot, 100);
        dirtyRoot.Load();
        dirtyRoot.LoadComplete();
        dirtyLeaf.Position = new Vector2(50, 50); // make the whole chain dirty once
    }

    /// <summary>
    /// Setting a property to its current value — should be a pure guard check, zero work.
    /// </summary>
    [Benchmark(Baseline = true)]
    public void SetPosition_SameValue()
    {
        deepLeaf.Position = deepLeaf.Position;
    }

    /// <summary>
    /// Changing a property on an already-dirty tree: the invalidation should early-exit
    /// without walking the 100-deep parent chain again.
    /// </summary>
    [Benchmark]
    public void SetPosition_NewValue_TreeAlreadyDirty()
    {
        frame++;
        dirtyLeaf.Position = new Vector2(frame % 2 == 0 ? 60 : 61, 50);
    }

    /// <summary>
    /// The full cycle on a clean 100-deep tree: one leaf change, then one update pass
    /// that cleans everything up. The honest cost of "one thing moved" at depth.
    /// </summary>
    [Benchmark]
    public void SetPosition_ThenFullUpdate_Deep100()
    {
        frame++;
        deepLeaf.Position = new Vector2(frame % 2 == 0 ? 10 : 11, 10);

        deepClock.CurrentTime += BenchmarkTree.FRAME_STEP_MS;
        deepRoot.UpdateSubTree();
    }
}
