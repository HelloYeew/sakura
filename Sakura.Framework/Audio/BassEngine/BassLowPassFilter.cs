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

    /// <summary>
    /// The cutoff frequency of the filter in Hertz.
    /// Frequencies above this value will be reduced.
    /// Max is typically half the sample rate (e.g., 22050 for a 44100Hz stream).
    /// </summary>
    public Reactive<double> CutoffFrequency { get; } = new Reactive<double>(22050.0);

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

        var parameters = new BQFParameters
        {
            lFilter = BQFType.LowPass,
            fCenter = (float)CutoffFrequency.Value,
            fBandwidth = 0,
            fQ = 0.707f
        };

        Logger.Debug($"Updating Low-Pass Filter parameters: Cutoff={parameters.fCenter}Hz, Q={parameters.fQ}");

        BassUtils.CheckError(Bass.FXSetParameters(fxHandle, parameters), "updating Low-Pass parameters");
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
