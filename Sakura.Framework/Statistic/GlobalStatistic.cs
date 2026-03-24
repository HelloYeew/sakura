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
            if (Value is float f)
                return f.ToString("0.##");
            if (Value is double d)
                return d.ToString("0.##");
            return Value.ToString() ?? string.Empty;
        }
    }

    public GlobalStatistic(string group, string name)
    {
        Group = group;
        Name = name;
    }

    public void Clear() => Value = default!;
}
