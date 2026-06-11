// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using BenchmarkDotNet.Attributes;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Benchmarks.Benchmarks;

/// <summary>
/// Measures the clock hierarchy. Every drawable owns a <see cref="FramedClock"/> that is
/// processed once per frame, so like the scheduler this is a per-drawable per-frame tax.
/// The chain benchmark models a deep parent chain (each drawable's clock sources its parent's).
/// </summary>
[MemoryDiagnoser]
public class ClockBenchmarks
{
    private ManualClock manualClock = null!;
    private FramedClock framedClock = null!;
    private FramedClock[] chain = null!;
    private Clock rawClock = null!;

    [GlobalSetup]
    public void Setup()
    {
        manualClock = new ManualClock();
        framedClock = new FramedClock(manualClock);

        chain = new FramedClock[10];
        IClock source = manualClock;
        for (int i = 0; i < chain.Length; i++)
        {
            chain[i] = new FramedClock(source);
            source = chain[i];
        }

        rawClock = new Clock(true);
    }

    /// <summary>
    /// One framed clock processing one frame, the unit cost paid per drawable per frame.
    /// </summary>
    [Benchmark(Baseline = true)]
    public double FramedClock_ProcessFrame()
    {
        manualClock.CurrentTime += 16.0;
        framedClock.ProcessFrame();
        return framedClock.CurrentTime;
    }

    /// <summary>
    /// A 10-deep chain of framed clocks processed in order — models the clock updates
    /// down a 10-deep drawable branch.
    /// </summary>
    [Benchmark]
    public double FramedClockChain10_ProcessFrame()
    {
        manualClock.CurrentTime += 16.0;
        for (int i = 0; i < chain.Length; i++)
            chain[i].ProcessFrame();
        return chain[^1].CurrentTime;
    }

    /// <summary>
    /// The thread-level clock (TimeSource-backed) snapshot, taken once per frame per thread.
    /// </summary>
    [Benchmark]
    public double Clock_Update()
    {
        rawClock.Update();
        return rawClock.CurrentTime;
    }
}
