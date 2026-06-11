// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using BenchmarkDotNet.Attributes;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Benchmarks.Benchmarks;

/// <summary>
/// Measures <c>GenerateDrawNodeSubtree</c> — the per-frame snapshot of the drawable tree into draw nodes.
/// </summary>
[MemoryDiagnoser]
public class DrawNodeGenerationBenchmarks
{
    private Container wideRoot = null!;
    private ManualClock wideClock = null!;

    private Container gridRoot = null!;
    private ManualClock gridClock = null!;
    private Container gridHolder = null!;

    private int frame;

    [GlobalSetup]
    public void Setup()
    {
        (wideRoot, wideClock) = BenchmarkTree.CreateRoot();
        BenchmarkTree.AddWide(wideRoot, 1000);
        BenchmarkTree.LoadAndSettle(wideRoot, wideClock);

        (gridRoot, gridClock) = BenchmarkTree.CreateRoot();
        gridHolder = BenchmarkTree.AddGrid(gridRoot, 10, 100);
        BenchmarkTree.LoadAndSettle(gridRoot, gridClock);
    }

    /// <summary>
    /// Pure topology walk over a clean wide tree: no drawable changed, so every node's
    /// ApplyState should be skipped. Measures sorting/list-rebuild overhead only.
    /// </summary>
    [Benchmark(Baseline = true)]
    public DrawNode Generate_CleanTree_Wide1000()
    {
        frame++;
        return wideRoot.GenerateDrawNodeSubtree(frame % 3);
    }

    /// <summary>
    /// Same as above over the nested grid (more containers → more per-container sorting).
    /// </summary>
    [Benchmark]
    public DrawNode Generate_CleanTree_Grid10x100()
    {
        frame++;
        return gridRoot.GenerateDrawNodeSubtree(frame % 3);
    }

    /// <summary>
    /// Full dirty path: the holder moves, the whole subtree re-applies state into nodes.
    /// This is the realistic per-frame cost when anything on screen is animating.
    /// </summary>
    [Benchmark]
    public DrawNode Generate_AfterFullInvalidation_Grid10x100()
    {
        frame++;
        gridHolder.Position = new Vector2(frame % 2 == 0 ? 0 : 1, 0);

        gridClock.CurrentTime += BenchmarkTree.FRAME_STEP_MS;
        gridRoot.UpdateSubTree();
        return gridRoot.GenerateDrawNodeSubtree(frame % 3);
    }
}
