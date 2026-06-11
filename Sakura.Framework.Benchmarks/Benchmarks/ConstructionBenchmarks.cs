// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using BenchmarkDotNet.Attributes;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Benchmarks.Benchmarks;

/// <summary>
/// Measures the cost of creating drawables and bringing them into a loaded hierarchy.
/// </summary>
[MemoryDiagnoser]
public class ConstructionBenchmarks
{
    private Container root = null!;
    private ManualClock clock = null!;

    [GlobalSetup]
    public void Setup()
    {
        (root, clock) = BenchmarkTree.CreateRoot();
        BenchmarkTree.LoadAndSettle(root, clock);
    }

    /// <summary>
    /// Raw construction: what does a single `new Box()` allocate before it touches a tree?
    /// </summary>
    [Benchmark(Baseline = true)]
    public Box NewBox() => new Box
    {
        Position = new Vector2(10, 10),
        Size = new Vector2(16, 16),
    };

    /// <summary>
    /// Raw construction of a container (adds a child list on top of the drawable cost).
    /// </summary>
    [Benchmark]
    public Container NewContainer() => new Container
    {
        Size = new Vector2(100, 100),
    };

    /// <summary>
    /// The full spawn path: construct, add to a loaded parent (triggers synchronous Load,
    /// dependency injection, clock re-wiring, topology invalidation), then remove.
    /// This approximates spawning one note mid-gameplay without pooling.
    /// </summary>
    [Benchmark]
    public void NewBox_AddLoadRemove()
    {
        var box = new Box
        {
            Position = new Vector2(10, 10),
            Size = new Vector2(16, 16),
        };

        root.Add(box);
        root.Remove(box);
    }

    /// <summary>
    /// Same spawn path for a container with one child — closer to a real note
    /// (note body + overlay) and exercises recursive load.
    /// </summary>
    [Benchmark]
    public void NewNestedContainer_AddLoadRemove()
    {
        var note = new Container
        {
            Position = new Vector2(10, 10),
            Size = new Vector2(90, 30),
        };
        note.Add(new Box
        {
            Size = new Vector2(1, 1),
            RelativeSizeAxes = Axes.Both,
        });

        root.Add(note);
        root.Remove(note);
    }
}
