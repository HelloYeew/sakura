// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using BenchmarkDotNet.Attributes;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Benchmarks.Benchmarks;

/// <summary>
/// Measures input handling through the central <see cref="InputManager"/>
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

    // Inert tree: boxes that handle nothing, so dispatch walks the full queue (worst case).
    private Container inertRoot = null!;
    private ManualClock inertClock = null!;

    // Tree with a front-most consuming handler under the cursor (realistic early-out).
    private Container handledRoot = null!;
    private ManualClock handledClock = null!;

    private InputManager manager = null!;

    private MouseEvent mouseMoveHit;
    private MouseEvent mouseMoveMiss;
    private MouseButtonEvent mouseDown;
    private MouseButtonEvent mouseUp;
    private KeyEvent keyEvent;
    private ScrollEvent scrollEvent;

    private static readonly Vector2 hit_point = new Vector2(640, 360);
    private static readonly Vector2 miss_point = new Vector2(-100, -100);

    // Consumed so dispatch results can't be optimised away
    private bool sink;

    [GlobalSetup]
    public void Setup()
    {
        (inertRoot, inertClock) = BenchmarkTree.CreateRoot();
        BenchmarkTree.AddGrid(inertRoot, Containers, ChildrenPerContainer);
        BenchmarkTree.LoadAndSettle(inertRoot, inertClock);

        (handledRoot, handledClock) = BenchmarkTree.CreateRoot();
        BenchmarkTree.AddGrid(handledRoot, Containers, ChildrenPerContainer);
        // Added last and full-screen so it sits at the front of both queues and covers the hit point,
        // giving every dispatch a first-entry consumer to short-circuit on.
        handledRoot.Add(new HandlerBox
        {
            Size = new Vector2(1280, 720),
        });
        BenchmarkTree.LoadAndSettle(handledRoot, handledClock);

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

    #region Queue building (per-frame tree walk)

    /// <summary>
    /// Rebuilds both queues for a point over the playfield. This is the per-frame cost that replaced
    /// the old recursive per-event traversal, and the number that matters for frame budget.
    /// </summary>
    [Benchmark(Baseline = true)]
    public void BuildQueues_OverTree() => manager.BuildQueues(inertRoot, hit_point);

    /// <summary>
    /// Rebuilds both queues for a point outside every drawable. The positional walk stops early
    /// (nothing receives), isolating the non-positional walk plus the positional reject cost.
    /// </summary>
    [Benchmark]
    public void BuildQueues_MissesTree() => manager.BuildQueues(inertRoot, miss_point);

    #endregion

    #region Dispatch, worst case: build + walk a queue that no one consumes

    /// <summary>
    /// Mouse move over the inert playfield: hover enter/leave reconciliation against a freshly built
    /// positional queue, nothing consuming. Subtract <see cref="BuildQueues_OverTree"/> for the
    /// dispatch-only cost.
    /// </summary>
    [Benchmark]
    public void MouseMove_OverTree()
    {
        manager.BuildQueues(inertRoot, hit_point);
        sink = manager.DispatchMouseMove(mouseMoveHit);
    }

    /// <summary>
    /// Mouse move outside all drawables — empty positional queue, pure hover-reconciliation overhead.
    /// </summary>
    [Benchmark]
    public void MouseMove_MissesTree()
    {
        manager.BuildQueues(inertRoot, miss_point);
        sink = manager.DispatchMouseMove(mouseMoveMiss);
    }

    /// <summary>
    /// A press → release pair over the inert tree, walking the positional queue front-to-back. The
    /// down sets the drag-capture target and the up releases it.
    /// </summary>
    [Benchmark]
    public void MouseDownUp_OverTree()
    {
        manager.BuildQueues(inertRoot, hit_point);
        sink = manager.DispatchMouseDown(mouseDown);
        sink = manager.DispatchMouseUp(mouseUp);
    }

    /// <summary>
    /// Worst-case key dispatch: nothing consumes, so the walk visits every entry of the
    /// non-positional queue (the whole tree). This is the row that scales linearly with tree size.
    /// </summary>
    [Benchmark]
    public void KeyDown_OverTree()
    {
        manager.BuildQueues(inertRoot, hit_point);
        sink = manager.DispatchKeyDown(keyEvent);
    }

    /// <summary>
    /// Scroll routed positionally over the inert tree; nothing consumes.
    /// </summary>
    [Benchmark]
    public void Scroll_OverTree()
    {
        manager.BuildQueues(inertRoot, hit_point);
        sink = manager.DispatchScroll(scrollEvent);
    }

    #endregion

    #region Dispatch, realistic early-out: a front-most handler consumes the event

    /// <summary>
    /// Key dispatch with a front-most consumer: the walk stops at the first queue entry. Compare with
    /// <see cref="KeyDown_OverTree"/> to see how much the consume-and-stop short-circuit saves, and
    /// whether the worst-case linear scaling actually bites when a real handler is present.
    /// </summary>
    [Benchmark]
    public void KeyDown_Handled()
    {
        manager.BuildQueues(handledRoot, hit_point);
        sink = manager.DispatchKeyDown(keyEvent);
    }

    /// <summary>
    /// Scroll dispatch with a front-most consumer under the cursor; stops at the first entry.
    /// </summary>
    [Benchmark]
    public void Scroll_Handled()
    {
        manager.BuildQueues(handledRoot, hit_point);
        sink = manager.DispatchScroll(scrollEvent);
    }

    #endregion

    /// <summary>
    /// A drawable that consumes every input it is offered, used to measure the early-out dispatch
    /// path. Front-most and full-screen in the handled tree, so it is the first queue entry for both
    /// positional and non-positional dispatch.
    /// </summary>
    private partial class HandlerBox : Box
    {
        public override bool OnKeyDown(KeyEvent e) => true;
        public override bool OnScroll(ScrollEvent e) => true;
        public override bool OnMouseDown(MouseButtonEvent e) => true;
        public override bool OnMouseUp(MouseButtonEvent e) => true;
    }
}
