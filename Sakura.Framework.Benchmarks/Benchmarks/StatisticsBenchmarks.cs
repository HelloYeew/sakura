// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using BenchmarkDotNet.Attributes;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Benchmarks.Benchmarks;

/// <summary>
/// Measures <see cref="GlobalStatistics"/> access patterns.
/// </summary>
[MemoryDiagnoser]
public class StatisticsBenchmarks
{
    private GlobalStatistic<int> cached = null!;
    private GlobalStatistic<int> perFrame = null!;
    private int plainField;

    [GlobalSetup]
    public void Setup()
    {
        cached = GlobalStatistics.Get<int>("Benchmarks", "Cached");
        perFrame = GlobalStatistics.Get<int>("Benchmarks", "Per Frame", StatisticKind.PerFrame);
    }

    /// <summary>
    /// The current hot-path pattern: full group + name lookup on every increment.
    /// </summary>
    [Benchmark(Baseline = true)]
    public void Get_ThenIncrement()
    {
        GlobalStatistics.Get<int>("Benchmarks", "Lookup").Value++;
    }

    /// <summary>
    /// "Resolve once into a static/readonly field, increment that" pattern.
    /// </summary>
    [Benchmark]
    public void CachedStatistic_Increment()
    {
        cached.Value++;
    }

    /// <summary>
    /// The per-frame producer path. Accumulating somewhere separate from the published figure is what
    /// keeps a reader on another thread from sampling a partial frame; this is what that costs against
    /// incrementing the published value directly, which is what it replaced.
    /// </summary>
    [Benchmark]
    public void PerFrameStatistic_Accumulate()
    {
        perFrame.Accumulator++;
    }

    /// <summary>
    /// The frame boundary. Runs once per frame per statistic, against the accumulate above which runs
    /// once per draw call so this wants to be cheap, but it does not want for much.
    /// </summary>
    [Benchmark]
    public void PerFrameStatistic_CompleteFrame()
    {
        perFrame.CompleteFrame();
    }

    /// <summary>
    /// What a reader pays. Once per statistic per refresh tick, not per frame.
    /// </summary>
    [Benchmark]
    public string PerFrameStatistic_Read() => perFrame.DisplayValue;

    /// <summary>
    /// Floor reference: a plain field increment.
    /// </summary>
    [Benchmark]
    public int PlainField_Increment() => ++plainField;
}
