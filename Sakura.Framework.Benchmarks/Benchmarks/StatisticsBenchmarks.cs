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
    private int plainField;

    [GlobalSetup]
    public void Setup()
    {
        cached = GlobalStatistics.Get<int>("Benchmarks", "Cached");
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
    /// Floor reference: a plain field increment.
    /// </summary>
    [Benchmark]
    public int PlainField_Increment() => ++plainField;
}
