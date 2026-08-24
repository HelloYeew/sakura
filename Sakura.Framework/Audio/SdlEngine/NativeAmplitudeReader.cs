// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics.CodeAnalysis;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// Reads a native node's metering and turns it into the <see cref="ChannelAmplitudes"/> a visualizer
/// expects, the update thread's half of what <see cref="AmplitudeTap"/> does for the managed mixer.
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
internal sealed class NativeAmplitudeReader
{
    private readonly float[] rawBins = new float[ChannelAmplitudes.AMPLITUDES_SIZE];
    private readonly float[] dampedBins = new float[ChannelAmplitudes.AMPLITUDES_SIZE];

    private long lastReadTick;
    private ChannelAmplitudes cached = ChannelAmplitudes.Empty;

    /// <summary>
    /// Returns the current snapshot for <paramref name="node"/>, recomputing at most once per
    /// <see cref="AmplitudeTap.CACHE_INTERVAL_MS"/>.
    /// </summary>
    /// <remarks>
    /// The returned value wraps a buffer this instance reuses, matching the managed tap and the BASS
    /// backend: callers must consume it before the next read rather than holding onto it.
    /// </remarks>
    public ChannelAmplitudes Read(SakuraAudioEngine engine, uint node)
    {
        long now = Environment.TickCount64;
        long elapsed = now - lastReadTick;

        if (elapsed < AmplitudeTap.CACHE_INTERVAL_MS)
            return cached;

        lastReadTick = now;

        if (!engine.TryGetState(node, out var state))
            return cached;

        int bins = engine.ReadSpectrum(node, rawBins);

        if (bins == 0)
        {
            // Nothing has passed through this node yet, so a visualizer should see an empty spectrum
            // rather than a decaying one.
            Array.Clear(dampedBins);
            cached = new ChannelAmplitudes(state.AmplitudeLeft, state.AmplitudeRight, dampedBins);
            return cached;
        }

        // Ease each bin toward its new reading instead of snapping, retaining less of the old value
        // the more time has passed since the previous read.
        float retain = (float)Math.Pow(AmplitudeTap.AMPLITUDE_RETAIN_PER_FRAME, elapsed / AmplitudeTap.AMPLITUDE_REFERENCE_FRAME_MS);

        for (int i = 0; i < dampedBins.Length; i++)
            dampedBins[i] = rawBins[i] + (dampedBins[i] - rawBins[i]) * retain;

        cached = new ChannelAmplitudes(state.AmplitudeLeft, state.AmplitudeRight, dampedBins);
        return cached;
    }

    /// <summary>
    /// Drops the damping history and the cached snapshot, so a stopped or seeked channel does not
    /// report a spectrum from where it used to be.
    /// </summary>
    public void Reset()
    {
        Array.Clear(dampedBins);
        lastReadTick = 0;
        cached = ChannelAmplitudes.Empty;
    }
}
