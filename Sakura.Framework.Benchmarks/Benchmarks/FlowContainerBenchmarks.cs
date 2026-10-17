// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using BenchmarkDotNet.Attributes;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Benchmarks.Benchmarks;

[MemoryDiagnoser]
public class FlowContainerBenchmarks
{
    /// <summary>
    /// Child counts to sweep. Small = typical UI (a row of buttons / stat labels),
    /// large = stress (e.g., a long scrolling list re-flowed every frame).
    /// </summary>
    [Params(4, 50, 500)]
    public int ChildCount;

    private bool flip;

    private Container singleLineRoot = null!;
    private ManualClock singleLineClock = null!;
    private FlowContainer singleLineFlow = null!;

    private Container wrappingRoot = null!;
    private ManualClock wrappingClock = null!;
    private FlowContainer wrappingFlow = null!;

    private Container verticalRoot = null!;
    private ManualClock verticalClock = null!;
    private FlowContainer verticalFlow = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Horizontal, auto-sized: never wraps — one line of N children.
        (singleLineRoot, singleLineClock) = BenchmarkTree.CreateRoot();
        singleLineFlow = buildFlow(FlowDirection.Horizontal, autoSize: true, wrapWidth: 0);
        singleLineRoot.Add(singleLineFlow);
        BenchmarkTree.LoadAndSettle(singleLineRoot, singleLineClock);

        // Horizontal, fixed width: children wrap into many lines.
        (wrappingRoot, wrappingClock) = BenchmarkTree.CreateRoot();
        wrappingFlow = buildFlow(FlowDirection.Horizontal, autoSize: false, wrapWidth: 400);
        wrappingRoot.Add(wrappingFlow);
        BenchmarkTree.LoadAndSettle(wrappingRoot, wrappingClock);

        // Vertical, auto-sized: a column of N children (each its own row conceptually).
        (verticalRoot, verticalClock) = BenchmarkTree.CreateRoot();
        verticalFlow = buildFlow(FlowDirection.Vertical, autoSize: true, wrapWidth: 0);
        verticalRoot.Add(verticalFlow);
        BenchmarkTree.LoadAndSettle(verticalRoot, verticalClock);
    }

    private FlowContainer buildFlow(FlowDirection direction, bool autoSize, float wrapWidth)
    {
        var flow = new FlowContainer
        {
            Direction = direction,
            Spacing = new Vector2(5, 5),
        };

        if (autoSize)
            flow.AutoSizeAxes = Axes.Both;
        else
            flow.Size = new Vector2(wrapWidth, 600);

        for (int i = 0; i < ChildCount; i++)
        {
            flow.Add(new Box
            {
                Size = new Vector2(40, 20),
            });
        }

        return flow;
    }

    /// <summary>
    /// Re-flows a single-line horizontal container. No wrapping, so this isolates the
    /// per-child positioning cost plus whatever the grouping pass allocates for one line.
    /// </summary>
    [Benchmark(Baseline = true)]
    public void Layout_Horizontal_SingleLine()
    {
        // A child resizing is what makes a flow lay out again.
        flip = !flip;
        singleLineFlow.Children[0].Width = flip ? 41 : 40;
        singleLineClock.CurrentTime += BenchmarkTree.FRAME_STEP_MS;
        singleLineRoot.UpdateSubTree();
    }

    /// <summary>
    /// Re-flows a fixed-width container whose children wrap into many lines.
    /// This is the heaviest case for the line-grouping pass.
    /// </summary>
    [Benchmark]
    public void Layout_Horizontal_Wrapping()
    {
        // A child resizing is what makes a flow lay out again.
        flip = !flip;
        wrappingFlow.Children[0].Width = flip ? 41 : 40;
        wrappingClock.CurrentTime += BenchmarkTree.FRAME_STEP_MS;
        wrappingRoot.UpdateSubTree();
    }

    /// <summary>
    /// Re-flows a vertical column of children.
    /// </summary>
    [Benchmark]
    public void Layout_Vertical_Column()
    {
        // A child resizing is what makes a flow lay out again.
        flip = !flip;
        verticalFlow.Children[0].Width = flip ? 41 : 40;
        verticalClock.CurrentTime += BenchmarkTree.FRAME_STEP_MS;
        verticalRoot.UpdateSubTree();
    }
}
