// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Audio;

/// <summary>
/// Interface for a playable audio channel (track, sample, etc.).
/// This is returned when a <see cref="Track"/> or <see cref="Sample"/> is played.
/// </summary>
public interface IAudioChannel : IDisposable
{
    /// <summary>
    /// Starts playing the audio on this channel.
    /// </summary>
    void Play();

    /// <summary>
    /// Stops playback and resets the position to the beginning (or RestartPoint if looping).
    /// </summary>
    void Stop();

    /// <summary>
    /// Pauses playback at the current position.
    /// </summary>
    void Pause();

    /// <summary>
    /// Fired when playback starts.
    /// </summary>
    event Action OnStart;

    /// <summary>
    /// Fired when playback is stopped (either manually or by reaching the end).
    /// </summary>
    event Action OnStop;

    /// <summary>
    /// Fired when the track ends. If looping, this fires just before it loops.
    /// </summary>
    event Action OnEnd;

    /// <summary>
    /// Gets whether the channel is currently playing.
    /// </summary>
    ReactiveBool IsRunning { get; }

    /// <summary>
    /// Gets or sets the playback volume (0.0 to 1.0).
    /// </summary>
    Reactive<double> Volume { get; }

    /// <summary>
    /// Gets or sets the playback frequency/pitch (1.0 is normal).
    /// </summary>
    Reactive<double> Frequency { get; }

    /// <summary>
    /// Gets or sets the stereo balance/pan (-1.0 left, 0.0 center, 1.0 right).
    /// </summary>
    Reactive<double> Balance { get; }

    /// <summary>
    /// Gets or sets the current playback position in milliseconds.
    /// </summary>
    double CurrentTime { get; set; }

    /// <summary>
    /// Gets the total length of the audio in milliseconds.
    /// </summary>
    double Length { get; }

    /// <summary>
    /// Gets or sets whether the audio should loop.
    /// </summary>
    bool Looping { get; set; }
}
