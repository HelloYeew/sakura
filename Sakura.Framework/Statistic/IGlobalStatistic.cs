// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Statistic;

public interface IGlobalStatistic
{
    string Group { get; }
    string Name { get; }
    string DisplayValue { get; }
    StatisticKind Kind { get; }
    StatisticUnit Unit { get; }
    double? NumericValue { get; }
    void Clear();

    /// <summary>
    /// Ends the frame a <see cref="StatisticKind.PerFrame"/> statistic has been accumulating, so
    /// <see cref="DisplayValue"/> reports a whole frame rather than however far the current one has
    /// got.
    /// </summary>
    void CompleteFrame();
}
