// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Threading;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Audio.BassEngine;

/// <summary>
/// Accounting for the unmanaged memory the BASS backend holds on behalf of loaded audio.
/// </summary>
/// <remarks>
/// Encoded audio handed to BASS as a memory stream has to stay at a fixed address for as long as
/// BASS reads it, so it lives outside the managed heap and is invisible to any managed profiler.
/// Without a number for it, a leak here looks exactly like "unmanaged memory grew and we do not
/// know why" — which is how the pinned-buffer problem went unnoticed in the first place.
/// </remarks>
internal static class BassAudioStatistics
{
    private static long nativeBufferBytes;

    /// <summary>
    /// Adjusts the reported total of unmanaged bytes holding encoded audio. Negative to subtract.
    /// </summary>
    internal static void AddNativeBufferBytes(long delta)
    {
        long total = Interlocked.Add(ref nativeBufferBytes, delta);
        GlobalStatistics.Get<long>("Audio", "Native Buffer Bytes").Value = total;
    }
}
