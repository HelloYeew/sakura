// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using BenchmarkDotNet.Attributes;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Benchmarks.Benchmarks;

/// <summary>
/// Measures input handling through the central <see cref="InputManager"/>. After the input-flow
/// refactor, the tree is no longer walked per-event: <see cref="InputManager.BuildQueues(Drawable, Vector2)"/>
/// walks it once per frame to build the positional and non-positional queues, and the per-event
/// <c>Dispatch*</c> methods then iterate those flat queues. Those are two very different costs, so
/// they are benchmarked separately:
///
/// <list type="bullet">
/// <item><description><b>BuildQueues</b> is the per-frame tree walk — the new hot path whose cost
/// scales with tree size, and where the real work (and any allocation) now lives.</description></item>
/// <item><description><b>Dispatch*</b> is the per-event cost — a flat queue iteration. Mouse events
/// can arrive at up to 1000 Hz and key events are the latency-critical path of a rhythm game.</description></item>
/// </list>
///
/// The dispatch benchmarks rebuild the queues in <see cref="IterationSetup"/> so they iterate a
/// populated queue (an empty queue returns instantly and measures nothing). Every benchmark stores
/// its result into a consumed field so the JIT cannot eliminate the call.
/// </summary>
[MemoryDiagnoser]
public class InputBenchmarks
{
    /// <summary>
    /// Number of column containers in the grid. Combined with <see cref="ChildrenPerContainer"/>
    /// this sets the tree size, which is what <see cref="BuildQueues"/> cost scales with.
    /// </summary>
    [Params(10)]
    public int Containers;

    [Params(10, 100)]
    public int ChildrenPerContainer;

    private Container root = null!;
    private ManualClock clock = null!;
    private InputManager manager = null!;

    private MouseEvent mouseMoveHit;
    private MouseEvent mouseMoveMiss;
    private MouseButtonEvent mouseDown;
    private MouseButtonEvent mouseUp;
    private KeyEvent keyEvent;
    private ScrollEvent scrollEvent;

    private static readonly Vector2 hit_point = new Vector2(640, 360);
    private static readonly Vector2 miss_point = new Vector2(-100, -100);

    // Consumed so dispatch results can't be optimised away (the source of the suspicious 0.0000 ns rows).
    private bool sink;

    [GlobalSetup]
    public void Setup()
    {
        (root, clock) = BenchmarkTree.CreateRoot();
        BenchmarkTree.AddGrid(root, Containers, ChildrenPerContainer);
        BenchmarkTree.LoadAndSettle(root, clock);

        manager = new InputManager();

        var hitState = new MouseState { Position = hit_point };
        var missState = new MouseState { Position = miss_point };

        mouseMoveHit = new MouseEvent(hitState, new Vector2(1, 0));
        mouseMoveMiss = new MouseEvent(missState, new Vector2(1, 0));
        mouseDown = new MouseButtonEvent(hitState, MouseButton.Left, 1);
        mouseUp = new MouseButtonEvent(hitState, MouseButton.Left, 1);
        keyEvent = new KeyEvent(Key.Z, KeyModifiers.None, false);
        scrollEvent = new ScrollEvent(hitState, new Vector2(0, 1));
    }

    // ---- Queue building (per-frame tree walk) ----------------------------------------------------

    /// <summary>
    /// Rebuilds both queues for a point over the playfield. This is the per-frame cost that replaced
    /// the old recursive per-event traversal, and the number that matters for frame budget.
    /// </summary>
    [Benchmark(Baseline = true)]
    public void BuildQueues_OverTree() => manager.BuildQueues(root, hit_point);

    /// <summary>
    /// Rebuilds both queues for a point outside every drawable. The positional walk stops early
    /// (nothing receives), isolating the non-positional walk plus the positional reject cost.
    /// </summary>
    [Benchmark]
    public void BuildQueues_MissesTree() => manager.BuildQueues(root, miss_point);

    // ---- Dispatch (per-event flat-queue iteration) -----------------------------------------------

    [IterationSetup(Targets = new[]
    {
        nameof(MouseMove_OverTree),
        nameof(MouseDownUp_OverTree),
        nameof(Scroll_OverTree),
        nameof(KeyDown_Dispatch)
    })]
    public void BuildQueuesForHit() => manager.BuildQueues(root, hit_point);

    [IterationSetup(Target = nameof(MouseMove_MissesTree))]
    public void BuildQueuesForMiss() => manager.BuildQueues(root, miss_point);

    /// <summary>
    /// A mouse move over the playfield: drag-capture delivery (none here) plus the hover
    /// enter/leave reconciliation against the freshly built positional queue.
    /// </summary>
    [Benchmark]
    public void MouseMove_OverTree() => sink = manager.DispatchMouseMove(mouseMoveHit);

    /// <summary>
    /// A mouse move outside all drawables — the positional queue is empty, so this measures the
    /// pure hover-reconciliation overhead with nothing under the cursor.
    /// </summary>
    [Benchmark]
    public void MouseMove_MissesTree() => sink = manager.DispatchMouseMove(mouseMoveMiss);

    /// <summary>
    /// A full press → release pair, walking the positional queue front-to-back. The down also sets
    /// the drag-capture target and the up releases it, exercising that bookkeeping.
    /// </summary>
    [Benchmark]
    public void MouseDownUp_OverTree()
    {
        sink = manager.DispatchMouseDown(mouseDown);
        sink = manager.DispatchMouseUp(mouseUp);
    }

    /// <summary>
    /// A key press dispatched down the non-positional queue. Replaces the old "broadcast to every
    /// drawable" measurement — keys now stop at the first consumer in the flat queue.
    /// </summary>
    [Benchmark]
    public void KeyDown_Dispatch() => sink = manager.DispatchKeyDown(keyEvent);

    /// <summary>
    /// A scroll routed positionally; the first queue entry under the cursor that handles it wins.
    /// </summary>
    [Benchmark]
    public void Scroll_OverTree() => sink = manager.DispatchScroll(scrollEvent);
}
