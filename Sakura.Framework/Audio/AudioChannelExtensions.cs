// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Threading.Tasks;
using Sakura.Framework.Audio.BassEngine;

namespace Sakura.Framework.Audio;

/// <summary>
/// Extension method for <see cref="IAudioChannel"/>
/// </summary>
public static class AudioChannelExtensions
{
    /// <summary>
    /// Smoothly transitions the volume of the channel to a target value over a specified duration.
    /// </summary>
    /// <param name="channel">The audio channel.</param>
    /// <param name="targetVolume">The desired end volume (0.0 to 1.0).</param>
    /// <param name="duration">How long the fade should take in milliseconds.</param>
    public static async Task FadeVolumeToAsync(this IAudioChannel channel, double targetVolume, int duration)
    {
        if (channel == null) return;

        if (duration <= 0)
        {
            channel.Volume.Value = targetVolume;
            return;
        }

        double startVolume = channel.Volume.Value;
        double volumeDifference = targetVolume - startVolume;

        // steps for a roughly 60 FPS update rate (approx 16ms per frame)
        int stepDelay = 16;
        int totalSteps = Math.Max(1, duration / stepDelay);

        for (int i = 1; i <= totalSteps; i++)
        {
            // If the channel was stopped or disposed during the fade, abort the fade
            if (!channel.IsRunning.Value && targetVolume > 0)
                break;

            await Task.Delay(stepDelay);

            double progress = (double)i / totalSteps;
            channel.Volume.Value = startVolume + (volumeDifference * progress);
        }

        channel.Volume.Value = targetVolume;
    }

    /// <summary>
    /// Sets volume to 0, starts playback, and fades in to the target volume.
    /// </summary>
    public static Task FadeInAsync(this IAudioChannel channel, int duration, double targetVolume = 1.0)
    {
        channel.Volume.Value = 0.0;
        channel.Play();
        return channel.FadeVolumeToAsync(targetVolume, duration);
    }

    /// <summary>
    /// Fades the volume to 0. Optionally stops the channel once the fade is complete.
    /// </summary>
    public static async Task FadeOutAsync(this IAudioChannel channel, int duration, bool stopAfterFade = true)
    {
        await channel.FadeVolumeToAsync(0.0, duration);

        if (stopAfterFade && channel.Volume.Value <= 0.01) // Check if volume actually hit 0
        {
            channel.Stop();
        }
    }

    /// <summary>
    /// Smoothly crossfades from the current channel to a new channel.
    /// </summary>
    /// <param name="currentChannel">The channel to fade out.</param>
    /// <param name="nextChannel">The channel to fade in.</param>
    /// <param name="duration">The duration of the crossfade in milliseconds.</param>
    /// <param name="targetVolume">The final volume for the new channel.</param>
    public static async Task CrossfadeToAsync(this IAudioChannel currentChannel, IAudioChannel nextChannel, int duration, double targetVolume = 1.0)
    {
        Task fadeOutTask = currentChannel?.FadeOutAsync(duration, stopAfterFade: true) ?? Task.CompletedTask;
        Task fadeInTask = nextChannel?.FadeInAsync(duration, targetVolume) ?? Task.CompletedTask;
        await Task.WhenAll(fadeOutTask, fadeInTask);
    }

    /// <summary>
    /// Applies a reactive Low-Pass filter to the target audio channel.
    /// </summary>
    /// <returns>The <see cref="BassLowPassFilter"/> instance to control the cutoff frequency.</returns>
    public static BassLowPassFilter AddLowPassFilter(this IAudioChannel channel)
    {
        if (channel is BassAudioChannel bassChannel)
        {
            // Attach the effect directly to the internal BASS handle
            return new BassLowPassFilter(bassChannel.ChannelHandle);
        }

        // Return null or a dummy filter if running in Headless mode
        return null;
    }
}
