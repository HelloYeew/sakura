// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using ManagedBass;
using Sakura.Framework.Audio.BassEngine;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Audio;

/// <summary>
/// A component that monitors a source audio channel and duck the volume
/// of a target audio channel based on the source's output level.
/// </summary>
public class AudioDucker : Component
{
    private readonly IAudioChannel source;
    private readonly IAudioChannel target;

    private readonly Reactive<double> targetVolumeBindable;

    /// <summary>
    /// How loud the source must be (0.0 to 1.0) to trigger maximum ducking.
    /// </summary>
    public float Threshold { get; set; } = 0.1f;

    /// <summary>
    /// The maximum amount to multiply the target's volume by when fully ducked (e.g., 0.3 = 30% volume).
    /// </summary>
    public double DuckMultiplier { get; set; } = 0.3;

    /// <summary>
    /// How fast the volume recovers when the source goes quiet.
    /// </summary>
    public float RecoverySpeed { get; set; } = 0.05f;

    private double currentDuckFactor = 1.0;

    public AudioDucker(IAudioChannel source, IAudioChannel target, Reactive.Reactive<double> targetVolumeBindable)
    {
        this.source = source;
        this.target = target;
        this.targetVolumeBindable = targetVolumeBindable;

        AlwaysPresent = true; // Ensure this updates even if hidden
    }

    public override void Update()
    {
        base.Update();

        if (source is BassAudioChannel bassSource)
        {
            // Get the current output level of the source channel
            int level = Bass.ChannelGetLevel(bassSource.ChannelHandle);

            if (level != -1)
            {
                // BASS packs left channel in low word, right channel in high word
                int left = level & 0xFFFF;
                int right = level >> 16;

                // Calculate the peak level from 0.0 to 1.0
                float peak = Math.Max(left, right) / 32768f;

                // Determine the target duck factor based on the threshold
                double targetDuckFactor = 1.0;
                if (peak > Threshold)
                {
                    // If it's loud, snap the duck factor down
                    targetDuckFactor = DuckMultiplier;
                }

                // Smoothly ease the current duck factor towards the target
                if (currentDuckFactor > targetDuckFactor)
                {
                    // Attack instantly
                    currentDuckFactor = targetDuckFactor;
                }
                else
                {
                    // Recover smoothly over time (incorporate Clock for frame-rate independence)
                    currentDuckFactor += RecoverySpeed * (Clock.ElapsedFrameTime / 16.66f);
                    currentDuckFactor = Math.Min(1.0, currentDuckFactor);
                }

                // Apply the ducked multiplier to the original volume value
                // Note: We bypass the Value setter to avoid triggering a feedback loop if needed,
                // but since we are writing to the channel directly here:
                if (target is BassAudioChannel bassTarget)
                {
                    // Calculate the final volume: (Framework Target Volume) * (Ducking Factor)
                    double finalVolume = targetVolumeBindable.Value * currentDuckFactor;

                    // Apply directly to BASS to avoid mutating the user's Reactive value
                    Bass.ChannelSetAttribute(bassTarget.ChannelHandle, ChannelAttribute.Volume, (float)finalVolume);
                }
            }
        }
    }
}
