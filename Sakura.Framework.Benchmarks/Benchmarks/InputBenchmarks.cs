// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using BenchmarkDotNet.Attributes;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Benchmarks.Benchmarks;

/// <summary>
/// Measures input event propagation through the drawable hierarchy. Mouse events can arrive
/// at up to 1000 Hz and key events are the latency-critical path of a rhythm game, so both
/// the time and the per-event allocations here matter a lot.
/// </summary>
[MemoryDiagnoser]
public class InputBenchmarks
{
    private Container root = null!;
    private ManualClock clock = null!;

    private MouseEvent mouseMoveHit;
    private MouseEvent mouseMoveMiss;
    private KeyEvent keyEvent;
    private ScrollEvent scrollEvent;

    [GlobalSetup]
    public void Setup()
    {
        (root, clock) = BenchmarkTree.CreateRoot();
        BenchmarkTree.AddGrid(root, 10, 100);
        BenchmarkTree.LoadAndSettle(root, clock);

        var hitState = new MouseState
        {
            Position = new Vector2(640, 360)
        };
        mouseMoveHit = new MouseEvent(hitState, new Vector2(1, 0));

        var missState = new MouseState
        {
            Position = new Vector2(-100, -100)
        };
        mouseMoveMiss = new MouseEvent(missState, new Vector2(1, 0));

        keyEvent = new KeyEvent(Key.Z, KeyModifiers.None, false);
        scrollEvent = new ScrollEvent(hitState, new Vector2(0, 1));
    }

    /// <summary>
    /// Mouse move over the playfield (hits drawables, triggers hover checks down the tree).
    /// </summary>
    [Benchmark(Baseline = true)]
    public bool MouseMove_OverTree() => root.OnMouseMove(mouseMoveHit);

    /// <summary>
    /// Mouse move outside all drawables — still traverses; measures the pure routing overhead.
    /// </summary>
    [Benchmark]
    public bool MouseMove_MissesTree() => root.OnMouseMove(mouseMoveMiss);

    /// <summary>
    /// A key press. Currently broadcast to every drawable in the tree; this measures
    /// the full press → handled round trip on a 1000-drawable scene.
    /// </summary>
    [Benchmark]
    public bool KeyDown_Broadcast() => root.OnKeyDown(keyEvent);

    /// <summary>
    /// Scroll routed positionally like clicks are.
    /// </summary>
    [Benchmark]
    public bool Scroll_OverTree() => root.OnScroll(scrollEvent);
}
