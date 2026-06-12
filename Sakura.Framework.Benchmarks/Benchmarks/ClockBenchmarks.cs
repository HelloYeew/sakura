// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using BenchmarkDotNet.Attributes;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Benchmarks.Benchmarks;

/// <summary>
/// Measures the clock hierarchy. Every drawable owns a <see cref="FramedClock"/> that is
/// processed once per frame, so like the scheduler this is a per-drawable per-frame tax.
/// The chain benchmark models a deep parent chain (each drawable's clock sources its parent's).
/// The gameplay benchmarks model the per-frame cost of the full rhythm-game timing chain
/// (source → decoupling → interpolating → offset) plus the per-input judgement lookup.
/// </summary>
[MemoryDiagnoser]
public class ClockBenchmarks
{
    private ManualClock manualClock = null!;
    private FramedClock framedClock = null!;
    private FramedClock[] chain = null!;
    private Clock rawClock = null!;

    private StopwatchClock stopwatchClock = null!;
    private FramedClock framedOverStopwatch = null!;

    private ManualClock interpolationSource = null!;
    private ManualClock interpolationReference = null!;
    private InterpolatingFramedClock interpolatingClock = null!;

    private StopwatchClock coupledSource = null!;
    private DecouplingFramedClock decouplingCoupled = null!;
    private StopwatchClock decoupledSource = null!;
    private DecouplingFramedClock decouplingDecoupled = null!;

    private StopwatchClock gameplaySource = null!;
    private GameplayClock gameplayClock = null!;

    private ThrottledFrameClock throttledClock = null!;
    private double throttledTime;

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

        stopwatchClock = new StopwatchClock(true);
        framedOverStopwatch = new FramedClock(stopwatchClock);

        // Deterministic interpolation: both source and reference are manual clocks
        // advanced in lockstep, keeping the clock on its smooth (interpolating) path.
        interpolationSource = new ManualClock();
        interpolationReference = new ManualClock();
        interpolatingClock = new InterpolatingFramedClock(interpolationSource, interpolationReference);

        // Coupled: a running source, so time is reported straight from the source.
        coupledSource = new StopwatchClock(true);
        decouplingCoupled = new DecouplingFramedClock(coupledSource);

        // Decoupled: a stopped source seeked far into negative (lead-in) time, so the
        // clock advances from the real-time reference and never reaches zero mid-run.
        decoupledSource = new StopwatchClock();
        decouplingDecoupled = new DecouplingFramedClock(decoupledSource);
        decouplingDecoupled.Seek(-1_000_000_000);
        decouplingDecoupled.Start();

        // The full recommended gameplay chain over a live source.
        gameplaySource = new StopwatchClock(true);
        gameplayClock = new GameplayClock(gameplaySource);

        throttledClock = new ThrottledFrameClock(1000);
    }

    /// <summary>
    /// Floor cost: one raw read of the shared timeline (Stopwatch.GetTimestamp + scale).
    /// Every live clock read bottoms out here.
    /// </summary>
    [Benchmark]
    public double TimeSource_CurrentTime() => TimeSource.CurrentTime;

    /// <summary>
    /// One live read of a running adjustable clock (derives from the time source on every read).
    /// </summary>
    [Benchmark]
    public double StopwatchClock_CurrentTime() => stopwatchClock.CurrentTime;

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
    /// A framed clock over a live stopwatch source — the realistic top-of-hierarchy case,
    /// where every source read costs a time-source query.
    /// </summary>
    [Benchmark]
    public double FramedClockOverStopwatch_ProcessFrame()
    {
        framedOverStopwatch.ProcessFrame();
        return framedOverStopwatch.CurrentTime;
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

    /// <summary>
    /// The interpolation stage on its smooth path (deterministic manual source/reference),
    /// isolating the interpolation math from time-source query cost.
    /// </summary>
    [Benchmark]
    public double InterpolatingFramedClock_ProcessFrame()
    {
        interpolationSource.CurrentTime += 16.0;
        interpolationReference.CurrentTime += 16.0;
        interpolatingClock.ProcessFrame();
        return interpolatingClock.CurrentTime;
    }

    /// <summary>
    /// The decoupling stage while the source is playing (time reported from the source).
    /// </summary>
    [Benchmark]
    public double DecouplingFramedClock_ProcessFrame_Coupled()
    {
        decouplingCoupled.ProcessFrame();
        return decouplingCoupled.CurrentTime;
    }

    /// <summary>
    /// The decoupling stage during lead-in (source stopped, advancing from the reference).
    /// </summary>
    [Benchmark]
    public double DecouplingFramedClock_ProcessFrame_Decoupled()
    {
        decouplingDecoupled.ProcessFrame();
        return decouplingDecoupled.CurrentTime;
    }

    /// <summary>
    /// One frame of the full recommended gameplay chain
    /// (decoupling → interpolating → offset snapshot) over a live source.
    /// This is the master-clock cost a rhythm game pays every update frame.
    /// </summary>
    [Benchmark]
    public double GameplayClock_ProcessFrame()
    {
        gameplayClock.ProcessFrame();
        return gameplayClock.CurrentTime;
    }

    /// <summary>
    /// Translating an input timestamp to gameplay time — the per-keypress judgement lookup.
    /// </summary>
    [Benchmark]
    public double GameplayClock_GetTimeAt()
        => gameplayClock.GetTimeAt(TimeSource.CurrentTime - 5.0);

    /// <summary>
    /// The frame-limiter check, paid once per loop iteration on throttled threads.
    /// </summary>
    [Benchmark]
    public bool ThrottledFrameClock_Process()
    {
        throttledTime += 16.0;
        return throttledClock.Process(throttledTime);
    }
}
