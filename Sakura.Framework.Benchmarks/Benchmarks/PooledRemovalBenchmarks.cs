// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using BenchmarkDotNet.Attributes;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Pooling;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Benchmarks.Benchmarks;

/// <summary>
/// The removal paths that run every frame in a live game rather than at teardown, plus the per-frame cost
/// the disposal queue adds to an update loop that is not removing anything.
/// </summary>
/// <remarks>
/// Unlike <see cref="DisposalBenchmarks"/> nothing here is consumed, so these run on the normal job and
/// are trustworthy at ns scale.
/// </remarks>
[MemoryDiagnoser]
public class PooledRemovalBenchmarks
{
    private const int churn_count = 64;

    private Container root = null!;
    private ManualClock clock = null!;
    private Container noteContainer = null!;
    private DrawablePool<BenchmarkNote> pool = null!;

    private Container reparentSource = null!;
    private Container reparentTarget = null!;
    private Box reparented = null!;

    private readonly BenchmarkNote[] checkedOut = new BenchmarkNote[churn_count];

    [GlobalSetup]
    public void Setup()
    {
        // Matches a running app: AppHost enables deferral for as long as it is pumping frames, so the
        // pooled path has to short-circuit with the queue live, not with it switched off.
        DrawableDisposalQueue.Enabled = true;
        DrawableDisposalQueue.Flush();

        (root, clock) = BenchmarkTree.CreateRoot();

        root.Add(pool = new DrawablePool<BenchmarkNote>(churn_count));
        root.Add(noteContainer = new Container { RelativeSizeAxes = Axes.Both, Size = new Vector2(1) });

        root.Add(reparentSource = new Container { Size = new Vector2(640, 360) });
        root.Add(reparentTarget = new Container { Position = new Vector2(640, 0), Size = new Vector2(640, 360) });

        BenchmarkTree.LoadAndSettle(root, clock);

        reparentSource.Add(reparented = new Box { Size = new Vector2(16) });
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        DrawableDisposalQueue.Flush();
        DrawableDisposalQueue.Enabled = false;
    }

    /// <summary>
    /// The per-frame tax on an update loop with nothing to dispose. Compare against zero: this is work
    /// that did not exist before.
    /// </summary>
    [Benchmark]
    public int ProcessEmptyQueue() => DrawableDisposalQueue.Process();

    /// <summary>
    /// Take <see cref="churn_count"/> drawables from the pool, show them, then remove them again — a
    /// playfield's visible set turning over. Repeatable precisely because removal returns pooled drawables
    /// to the pool instead of disposing them, which is what this is here to confirm.
    /// </summary>
    [Benchmark]
    public void PooledChurn()
    {
        for (int i = 0; i < churn_count; i++)
        {
            var note = pool.Get();
            checkedOut[i] = note;
            noteContainer.Add(note);
        }

        for (int i = 0; i < churn_count; i++)
            noteContainer.Remove(checkedOut[i]);
    }

    /// <summary>
    /// The reparenting path: removed with <c>dispose: false</c> and added elsewhere. This is what a call
    /// site moving a loaded subtree between parents now costs, and the only removal shape that still opts
    /// out of disposal by the remover's choice.
    /// </summary>
    [Benchmark]
    public void ReparentWithoutDisposal()
    {
        reparentSource.Remove(reparented, false);
        reparentTarget.Add(reparented);

        reparentTarget.Remove(reparented, false);
        reparentSource.Add(reparented);
    }

    /// <summary>
    /// A pooled drawable, minimal but not empty — a bare <see cref="PoolableDrawable"/> would make the
    /// cascade's per-child work look cheaper than any real note.
    /// </summary>
    private partial class BenchmarkNote : PoolableDrawable
    {
        public BenchmarkNote()
        {
            Size = new Vector2(48, 16);

            Add(new Box { RelativeSizeAxes = Axes.Both, Size = new Vector2(1) });
        }
    }
}
