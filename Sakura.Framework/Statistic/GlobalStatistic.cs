// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Statistic;

public class GlobalStatistic<T> : IGlobalStatistic
{
    public string Group { get; }
    public string Name { get; }
    public T Value { get; set; }

    public StatisticKind Kind { get; internal set; }
    public StatisticUnit Unit { get; internal set; }

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

            case StatisticUnit.Milliseconds:
                return $"{numeric.Value:N2} ms";

            case StatisticUnit.Microseconds:
                return $"{numeric.Value:N0} µs";

            case StatisticUnit.Percent:
                return $"{numeric.Value:N2}%";

            case StatisticUnit.Hertz:
                return $"{numeric.Value:N0} Hz";

            default:
                return Value switch
                {
                    int i => i.ToString("N0"),
                    long l => l.ToString("N0"),
                    float f => f.ToString("N2"),
                    double d => d.ToString("N2"),
                    decimal dec => dec.ToString("N2"),
                    _ => Value!.ToString() ?? string.Empty
                };
        }
    }
    
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

    public void Clear() => Value = default!;
}
