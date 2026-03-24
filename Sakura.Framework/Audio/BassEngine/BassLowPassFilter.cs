// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using ManagedBass;
using ManagedBass.Fx;
using Sakura.Framework.Logging;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Audio.BassEngine;

/// <summary>
/// Apply low-pass filter effect to BASS channel or mixer.
/// </summary>
public class BassLowPassFilter : IDisposable
{
    private readonly int targetChannelHandle;
    private int fxHandle;
    private bool isDisposed;

    public static double DefaultCutoffFrequency => 20000.0; // 44.1kHz

    /// <summary>
    /// The cutoff frequency of the filter in Hertz.
    /// Frequencies above this value will be reduced.
    /// Max is typically half the sample rate (e.g., 22050 for a 44100Hz stream).
    /// </summary>
    public Reactive<double> CutoffFrequency { get; } = new Reactive<double>(DefaultCutoffFrequency);

    public BassLowPassFilter(int channelHandle, int priority = 0)
    {
        targetChannelHandle = channelHandle;

        // EffectType.BQF is a BiQuad Filter built into BASS.
        fxHandle = Bass.ChannelSetFX(targetChannelHandle, EffectType.BQF, priority);

        if (fxHandle == 0)
        {
            Logger.Error($"BASS Error: {Bass.LastError} while adding Low-Pass Filter.", new BassException(Bass.LastError));
            return;
        }

        // Apply initial parameters
        updateParameters();

        // Bind the reactive property so the filter updates dynamically when the value changes
        CutoffFrequency.ValueChanged += _ => updateParameters();
    }

    private void updateParameters()
    {
        if (fxHandle == 0 || isDisposed) return;

        Bass.ChannelGetInfo(targetChannelHandle, out ChannelInfo info);

        double maxAllowedFreq = info.Frequency / 2.0 - 1.0;

        float safeCutoff = (float)Math.Clamp(CutoffFrequency.Value, 1.0, maxAllowedFreq);

        var parameters = new BQFParameters
        {
            lFilter = BQFType.LowPass,
            fCenter = safeCutoff,
            fBandwidth = 0f,
            fQ = 0.707f,
            fS = 0f,
            fGain = 0f
        };

        Logger.Debug($"Updating Low-Pass Filter parameters: Cutoff={parameters.fCenter}Hz, Q={parameters.fQ}");

        BassUtils.CheckError(Bass.FXSetParameters(fxHandle, parameters), "updating Low-Pass parameters");
    }

    public void Reset()
    {
        if (fxHandle == 0 || isDisposed) return;

        CutoffFrequency.Value = DefaultCutoffFrequency;
    }

    public void Dispose()
    {
        if (isDisposed) return;

        if (fxHandle != 0)
        {
            Bass.ChannelRemoveFX(targetChannelHandle, fxHandle);
            fxHandle = 0;
        }

        isDisposed = true;
    }
}
