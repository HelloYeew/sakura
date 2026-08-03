// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Statistic;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// Accounting for the reference-counted texture sharing behind <see cref="ITextureManager.CreateFromStream"/>.
/// </summary>
internal static class SharedTextureStatistics
{
    private static readonly GlobalStatistic<int> stat_keys = GlobalStatistics.Get<int>("Textures", "Shared Keys");
    private static readonly GlobalStatistic<long> stat_hits = GlobalStatistics.Get<long>("Textures", "Shared Hits");

    /// <summary>
    /// Records a request satisfied by an already-loaded texture, i.e. a decode and upload avoided.
    /// </summary>
    internal static void RecordHit() => stat_hits.Value++;

    /// <summary>
    /// Publishes how many distinct shared keys are currently held.
    /// </summary>
    internal static void SetKeyCount(int count) => stat_keys.Value = count;
}
