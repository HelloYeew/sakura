// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using BenchmarkDotNet.Attributes;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Benchmarks.Benchmarks;

/// <summary>
/// Isolates the per-drawable geometry pipeline: matrix construction + vertex generation
/// (<c>UpdateTransforms</c>), and the draw-node snapshot (<c>ApplyState</c>, which copies
/// vertices). These run for every dirty drawable every frame, so their unit cost directly
/// scales the numbers seen in <see cref="UpdateLoopBenchmarks"/>.
/// </summary>
[MemoryDiagnoser]
public class VertexGenerationBenchmarks
{
    /// <summary>
    /// Exposes the protected-internal geometry entry points for measurement.
    /// </summary>
    private sealed class ExposedBox : Box
    {
        public void RunUpdateTransforms() => UpdateTransforms();
    }

    private Container root = null!;
    private ManualClock clock = null!;
    private ExposedBox plainBox = null!;
    private ExposedBox rotatedBox = null!;
    private DrawNode node = null!;

    [GlobalSetup]
    public void Setup()
    {
        (root, clock) = BenchmarkTree.CreateRoot();

        plainBox = new ExposedBox
        {
            Position = new Vector2(100, 100),
            Size = new Vector2(64, 64),
        };

        rotatedBox = new ExposedBox
        {
            Position = new Vector2(100, 100),
            Size = new Vector2(64, 64),
            Rotation = 30,
            Scale = new Vector2(1.5f, 1.5f),
            Shear = new Vector2(0.2f, 0),
        };

        root.Add(plainBox);
        root.Add(rotatedBox);
        BenchmarkTree.LoadAndSettle(root, clock);

        node = plainBox.GenerateDrawNode(0);
    }

    /// <summary>
    /// Matrix build + 4 corner transforms + 6 vertex writes for an axis-aligned box.
    /// The unit of work behind every positional invalidation.
    /// </summary>
    [Benchmark(Baseline = true)]
    public void UpdateTransforms_PlainBox() => plainBox.RunUpdateTransforms();

    /// <summary>
    /// Same with rotation, scale and shear active (extra matrix multiplies).
    /// </summary>
    [Benchmark]
    public void UpdateTransforms_RotatedShearedBox() => rotatedBox.RunUpdateTransforms();

    /// <summary>
    /// The draw-node snapshot for one drawable: state copy + vertex array copy.
    /// Runs on the update thread for every invalidated drawable every frame.
    /// </summary>
    [Benchmark]
    public void DrawNode_ApplyState() => node.ApplyState(plainBox);
}
