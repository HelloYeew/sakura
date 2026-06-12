// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Benchmarks.Benchmarks;

/// <summary>
/// Measures one update-thread pass (<c>UpdateSubTree</c>) over different tree shapes
/// and dirtiness levels. The Clean/OneChildMoves/AllChildrenMove trio quantifies how much
/// the invalidation cascade amplifies a single change into whole-tree recomputation.
/// </summary>
[MemoryDiagnoser]
public class UpdateLoopBenchmarks
{
    public enum TreeShape
    {
        /// <summary>1 container × 1000 boxes.</summary>
        Wide1000,

        /// <summary>10 containers × 100 boxes.</summary>
        Grid10X100,

        /// <summary>100 nested containers with one leaf box.</summary>
        Deep100,
    }

    [Params(TreeShape.Wide1000, TreeShape.Grid10X100, TreeShape.Deep100)]
    public TreeShape Shape;

    private Container root = null!;
    private ManualClock clock = null!;
    private readonly List<Box> boxes = new List<Box>();
    private Box movingBox = null!;
    private int frame;

    [GlobalSetup]
    public void Setup()
    {
        (root, clock) = BenchmarkTree.CreateRoot();
        boxes.Clear();

        switch (Shape)
        {
            case TreeShape.Wide1000:
                collectBoxes(BenchmarkTree.AddWide(root, 1000));
                break;

            case TreeShape.Grid10X100:
                collectBoxes(BenchmarkTree.AddGrid(root, 10, 100));
                break;

            case TreeShape.Deep100:
                var (_, leaf) = BenchmarkTree.AddDeep(root, 100);
                boxes.Add(leaf);
                break;
        }

        movingBox = boxes[boxes.Count / 2];
        BenchmarkTree.LoadAndSettle(root, clock);
    }

    private void collectBoxes(Container container)
    {
        foreach (var child in container.Children)
        {
            if (child is Box box)
                boxes.Add(box);
            else if (child is Container nested)
                collectBoxes(nested);
        }
    }

    /// <summary>
    /// Nothing changed since last frame. This is the floor: per-drawable clock updates,
    /// scheduler updates and invalidation checks with no actual recomputation.
    /// </summary>
    [Benchmark(Baseline = true)]
    public void UpdateSubTree_Clean()
    {
        clock.CurrentTime += BenchmarkTree.FRAME_STEP_MS;
        root.UpdateSubTree();
    }

    /// <summary>
    /// A single box moves. In an ideal framework this costs little more than Clean;
    /// the gap shows how far one invalidation spreads through the tree.
    /// </summary>
    [Benchmark]
    public void UpdateSubTree_OneChildMoves()
    {
        frame++;
        movingBox.Position = new Vector2(frame % 2 == 0 ? 100 : 101, movingBox.Position.Y);

        clock.CurrentTime += BenchmarkTree.FRAME_STEP_MS;
        root.UpdateSubTree();
    }

    /// <summary>
    /// Every box moves every frame — the rhythm-game steady state where all notes scroll.
    /// </summary>
    [Benchmark]
    public void UpdateSubTree_AllChildrenMove()
    {
        frame++;
        float offset = frame % 2 == 0 ? 0 : 1;

        for (int i = 0; i < boxes.Count; i++)
        {
            var box = boxes[i];
            box.Position = new Vector2(box.Position.X, i % 16 * 40 + offset);
        }

        clock.CurrentTime += BenchmarkTree.FRAME_STEP_MS;
        root.UpdateSubTree();
    }

    /// <summary>
    /// Every box fades (alpha-only change) every frame. Quantifies how expensive a
    /// colour-only invalidation is — ideally far cheaper than a positional one.
    /// </summary>
    [Benchmark]
    public void UpdateSubTree_AllChildrenFade()
    {
        frame++;
        float alpha = frame % 2 == 0 ? 0.5f : 0.6f;

        for (int i = 0; i < boxes.Count; i++)
            boxes[i].Alpha = alpha;

        clock.CurrentTime += BenchmarkTree.FRAME_STEP_MS;
        root.UpdateSubTree();
    }
}
