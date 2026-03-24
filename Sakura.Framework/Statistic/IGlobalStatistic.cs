// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Statistic;

public interface IGlobalStatistic
{
    string Group { get; }
    string Name { get; }
    string DisplayValue { get; }
    void Clear();
}
