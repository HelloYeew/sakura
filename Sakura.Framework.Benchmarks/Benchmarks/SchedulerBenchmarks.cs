// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using BenchmarkDotNet.Attributes;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Benchmarks.Benchmarks;

/// <summary>
/// Measures <see cref="Scheduler"/> since it's called every frame.
/// </summary>
[MemoryDiagnoser]
public class SchedulerBenchmarks
{
    private ManualClock clock = null!;
    private Scheduler emptyScheduler = null!;
    private Scheduler pendingFutureScheduler = null!;
    private Scheduler churnScheduler = null!;

    private static void noop() { }

    [GlobalSetup]
    public void Setup()
    {
        clock = new ManualClock { CurrentTime = 0 };

        emptyScheduler = new Scheduler(clock);

        // 100 tasks that are always in the future — sorted once, then scanned per update.
        pendingFutureScheduler = new Scheduler(clock);
        for (int i = 0; i < 100; i++)
            pendingFutureScheduler.AddDelayed(noop, 1e12 + i);
        pendingFutureScheduler.Update();

        churnScheduler = new Scheduler(clock);
    }

    /// <summary>
    /// The per-drawable per-frame cost: updating a scheduler with nothing to do.
    /// Multiply this by the drawable count to get the per-frame tax.
    /// </summary>
    [Benchmark(Baseline = true)]
    public void Update_Empty() => emptyScheduler.Update();

    /// <summary>
    /// Updating with 100 pending future tasks (none due). Should be near-constant time
    /// thanks to the sorted early-exit.
    /// </summary>
    [Benchmark]
    public void Update_100PendingFutureTasks() => pendingFutureScheduler.Update();

    /// <summary>
    /// Scheduling churn: add 100 delayed tasks and run one update (which sorts the
    /// pending list), then clear. Tracks allocation and sorting cost of AddDelayed.
    /// </summary>
    [Benchmark]
    public void Add100Delayed_UpdateOnce_Clear()
    {
        for (int i = 0; i < 100; i++)
            churnScheduler.AddDelayed(noop, 1e12 - i);

        churnScheduler.Update();
        churnScheduler.Clear();
    }

    /// <summary>
    /// Tasks that are due and execute immediately — e.g. cross-thread callbacks
    /// delivered via Schedule().
    /// </summary>
    [Benchmark]
    public void Add10Immediate_UpdateExecutes()
    {
        for (int i = 0; i < 10; i++)
            churnScheduler.Add(noop);

        churnScheduler.Update();
    }
}
