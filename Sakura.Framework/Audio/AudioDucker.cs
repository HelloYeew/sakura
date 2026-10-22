// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Audio;

/// <summary>
/// A component that monitors a source audio channel and duck the volume
/// of a target audio channel based on the source's output level.
/// </summary>
/// <remarks>
/// The target is turned down under its own <see cref="IAudioChannel.Volume"/>, which is left as its owner set it, on
/// every backend. Removed or disposed, the ducker lets go of it.
/// </remarks>
public partial class AudioDucker : Component
{
    private readonly IAudioChannel source;
    private readonly IAudioChannel target;

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

    /// <summary>
    /// The current ducking factor applied to the target's volume (1.0 = no ducking,
    /// down to <see cref="DuckMultiplier"/> when fully ducked). Useful for meters/UI and testing.
    /// </summary>
    public double CurrentDuckFactor => currentDuckFactor;

    public AudioDucker(IAudioChannel source, IAudioChannel target)
    {
        this.source = source;
        this.target = target;

        AlwaysPresent = true; // Ensure this updates even if hidden
    }

    [Obsolete("The target is ducked under its own volume, which needn't be passed: use the constructor that takes the two channels.")]
    public AudioDucker(IAudioChannel source, IAudioChannel target, Reactive.Reactive<double> targetVolumeBindable)
        : this(source, target)
    {
    }

    public override void Update()
    {
        base.Update();

        float peak = Math.Max(source.AmplitudeLeft, source.AmplitudeRight);

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

        if (target is IDuckable duckable)
            duckable.Ducking = currentDuckFactor;
    }

    protected override void Dispose(bool isDisposing)
    {
        if (target is IDuckable duckable)
            duckable.Ducking = 1;

        base.Dispose(isDisposing);
    }
}
