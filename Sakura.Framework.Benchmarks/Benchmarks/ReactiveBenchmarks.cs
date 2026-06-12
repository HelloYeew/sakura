// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using BenchmarkDotNet.Attributes;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Benchmarks.Benchmarks;

/// <summary>
/// Benchmark for <see cref="Reactive{T}"/> system.
/// </summary>
[MemoryDiagnoser]
public class ReactiveBenchmarks
{
    private const int chain_length = 10;

    private Reactive<int> unobserved = null!;
    private Reactive<int> oneSubscriber = null!;
    private Reactive<int> eightSubscribers = null!;
    private Reactive<string> stringValue = null!;
    private Reactive<int>[] chain = null!;
    private ReactiveNumber<float> number = null!;

    private int counter;
    private int sink;

    [GlobalSetup]
    public void Setup()
    {
        unobserved = new Reactive<int>(0);

        oneSubscriber = new Reactive<int>(0);
        oneSubscriber.ValueChanged += e => sink += e.NewValue;

        eightSubscribers = new Reactive<int>(0);
        for (int i = 0; i < 8; i++)
            eightSubscribers.ValueChanged += e => sink += e.NewValue;

        stringValue = new Reactive<string>("a");

        // A chain of bindings: chain[i] follows chain[i - 1].
        chain = new Reactive<int>[chain_length];
        chain[0] = new Reactive<int>(0);
        for (int i = 1; i < chain_length; i++)
        {
            chain[i] = new Reactive<int>(0);
            chain[i].BindTo(chain[i - 1]);
        }

        number = new ReactiveNumber<float>(0)
        {
            MinValue = 0,
            MaxValue = 1_000_000
        };
    }

    /// <summary>
    /// Raw construction: what a single reactive allocates up front
    /// (bindings list, event backing, default value storage).
    /// </summary>
    [Benchmark]
    public Reactive<int> Construct() => new Reactive<int>(0);

    /// <summary>
    /// The most common case by far: setting a value nobody is listening to
    /// (e.g. internal state, config values without observers).
    /// </summary>
    [Benchmark(Baseline = true)]
    public void SetValue_NoSubscribers()
    {
        unobserved.Value = ++counter;
    }

    /// <summary>
    /// Setting the same value repeatedly — must early-exit on the equality check.
    /// </summary>
    [Benchmark]
    public void SetValue_SameValue()
    {
        unobserved.Value = counter;
    }

    /// <summary>
    /// One listener: the typical UI binding (label following a value).
    /// </summary>
    [Benchmark]
    public void SetValue_OneSubscriber()
    {
        oneSubscriber.Value = ++counter;
    }

    /// <summary>
    /// Several listeners on one value (e.g. master volume observed by many components).
    /// </summary>
    [Benchmark]
    public void SetValue_EightSubscribers()
    {
        eightSubscribers.Value = ++counter;
    }

    /// <summary>
    /// Reference-type path: string equality on every set.
    /// </summary>
    [Benchmark]
    public void SetValue_String()
    {
        stringValue.Value = (counter++ & 1) == 0 ? "alpha" : "beta";
    }

    /// <summary>
    /// Propagation through a 10-deep binding chain — each hop is an event invoke,
    /// an equality check and a re-trigger.
    /// </summary>
    [Benchmark]
    public int BindChain10_Propagate()
    {
        chain[0].Value = ++counter;
        return chain[chain_length - 1].Value;
    }

    /// <summary>
    /// Bind/unbind churn, as happens when pooled UI is rewired to new data
    /// (e.g. score displays binding to a fresh gameplay state).
    /// </summary>
    [Benchmark]
    public void BindUnbind_Churn()
    {
        var target = new Reactive<int>(0);
        target.BindTo(unobserved);
        target.UnbindFrom(unobserved);
    }

    /// <summary>
    /// Numeric variant with min/max range active (slider-style usage).
    /// </summary>
    [Benchmark]
    public void ReactiveNumber_SetValue()
    {
        number.Value = ++counter % 1_000_000;
    }

    /// <summary>
    /// Numeric variant with a precision step configured (settings sliders with
    /// e.g. 0.1 steps). Exercises the rounding path on every set.
    /// </summary>
    [Benchmark]
    public void ReactiveNumber_SetValue_WithPrecision()
    {
        // (At baseline time the Precision setter had a bug — the field was never assigned —
        // so the baseline run of this benchmark measured the unrounded path.)
        number.Precision = 0.5f;
        number.Value = ++counter % 1_000_000 + 0.3f;
    }
}
