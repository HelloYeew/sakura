// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Statistic;

public class GlobalStatistic<T> : IGlobalStatistic
{
    /// <summary>
    /// Whether <typeparamref name="T"/> is a whole-number type, and so formats without decimals. A
    /// static fact about the closed generic, computed once rather than re-derived from the value on
    /// every read.
    /// </summary>
    private static readonly bool integral = typeof(T) == typeof(int) || typeof(T) == typeof(long);

    public string Group { get; }
    public string Name { get; }
    public T Value { get; set; }

    public StatisticKind Kind { get; internal set; }
    public StatisticUnit Unit { get; internal set; }

    /// <summary>
    /// Where a <see cref="StatisticKind.PerFrame"/> producer accumulates the frame, it is part-way
    /// through. Nothing should read this to find out how much of something happened that is
    /// <see cref="Value"/>, which holds the last completed frame.
    /// </summary>
    public T Accumulator { get; set; } = default!;

    public string DisplayValue
    {
        get
        {
            if (Value == null)
                return "null";

            string formatted = format();
            return Kind == StatisticKind.PerFrame ? $"{formatted} /frame" : formatted;
        }
    }

    private string format()
    {
        double? numeric = NumericValue;

        if (numeric == null)
            return Value!.ToString() ?? string.Empty;

        switch (Unit)
        {
            case StatisticUnit.Bytes:
                return formatBytes(numeric.Value);

            case StatisticUnit.BytesPerSecond:
                return $"{formatBytes(numeric.Value)}/s";

            case StatisticUnit.Milliseconds:
                return $"{numeric.Value:N2} ms";

            case StatisticUnit.Microseconds:
                return $"{numeric.Value:N0} µs";

            case StatisticUnit.Percent:
                return $"{numeric.Value:N2}%";

            case StatisticUnit.Hertz:
                return $"{numeric.Value:N0} Hz";

            default:
                // numeric is already in hand from the null check above, so this does not go back to
                // the value and box it a second time.
                return numeric.Value.ToString(integral ? "N0" : "N2");
        }
    }

    // TODO: Move to extensions maybe better??
    private static string formatBytes(double bytes)
    {
        double abs = Math.Abs(bytes);

        if (abs < 1024)
            return $"{bytes:N0} B";

        if (abs < 1024d * 1024)
            return $"{bytes / 1024:N1} KB";

        if (abs < 1024d * 1024 * 1024)
            return $"{bytes / (1024d * 1024):N1} MB";

        return $"{bytes / (1024d * 1024 * 1024):N2} GB";
    }

    public double? NumericValue => Value switch
    {
        int i => i,
        long l => l,
        float f => f,
        double d => d,
        decimal dec => (double)dec,
        _ => null
    };

    public GlobalStatistic(string group, string name, StatisticKind kind = StatisticKind.Gauge, StatisticUnit unit = StatisticUnit.None)
    {
        Group = group;
        Name = name;
        Kind = kind;
        Unit = unit;
    }

    /// <summary>
    /// Ends the frame this statistic has been accumulating: <see cref="Accumulator"/> becomes
    /// <see cref="Value"/>, and accumulation restarts from zero. Called by the producer at its frame
    /// boundary, in place of zeroing the value outright.
    /// </summary>
    public void CompleteFrame()
    {
        Value = Accumulator;
        Accumulator = default!;
    }

    public void Clear()
    {
        Value = default!;
        Accumulator = default!;
    }
}
