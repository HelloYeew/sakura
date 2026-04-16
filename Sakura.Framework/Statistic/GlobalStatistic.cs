// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Statistic;

public class GlobalStatistic<T> : IGlobalStatistic
{
    public string Group { get; }
    public string Name { get; }
    public T Value { get; set; }

    public string DisplayValue
    {
        get
        {
            if (Value == null)
                return "null";

            return Value switch
            {
                int i => i.ToString("N0"),
                long l => l.ToString("N0"),
                float f => f.ToString("N2"),
                double d => d.ToString("N2"),
                decimal dec => dec.ToString("N2"),
                _ => Value.ToString() ?? string.Empty
            };
        }
    }

    public GlobalStatistic(string group, string name)
    {
        Group = group;
        Name = name;
    }

    public void Clear() => Value = default!;
}
