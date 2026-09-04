// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics.CodeAnalysis;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// The slice of the audio engine a channel needs: the output format it must produce, and a way to
/// get work onto the audio thread.
/// </summary>
/// <remarks>
/// An interface rather than a direct reference to the manager so a voice can be exercised without
/// opening a device, the mix maths is entirely testable against a stub, and only the manager
/// itself needs real hardware.
/// </remarks>
[SuppressMessage("ReSharper", "InconsistentNaming")]
internal interface ISDLAudioContext
{
    /// <summary>
    /// Output sample rate in Hz. Every <see cref="IPcmSource"/> feeding a channel is already at this rate.
    /// </summary>
    int SampleRate { get; }

    /// <summary>
    /// Output channel count.
    /// </summary>
    int Channels { get; }

    /// <summary>
    /// How far ahead of the listener the audio already handed to the device is, in milliseconds.
    /// </summary>
    /// <remarks>
    /// Sampled once a frame by the manager and read by every channel, so a channel subtracting it
    /// from its own cursor reports what is audible rather than what has been mixed, and every
    /// channel in a frame agrees. Zero is a valid answer and is what a stub or a drained device
    /// gives.
    /// </remarks>
    double OutputLatencyMs { get; }

    /// <summary>
    /// Queues work for the audio thread. Channel state changes and user-facing events go through
    /// here rather than firing on the mix thread, matching the BASS backend.
    /// </summary>
    void EnqueueAction(Action action);

    /// <summary>
    /// Raises a user-facing channel event (<see cref="IAudioChannel.OnStart"/>,
    /// <see cref="IAudioChannel.OnStop"/>, <see cref="IAudioChannel.OnEnd"/>) where the caller can act
    /// on it, which is the update thread in a hosted app.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="EnqueueAction"/>, which gets work onto the audio thread. This gets
    /// work back off it by <see cref="IAudioManager.Update"/> runs there, so a channel that fired its
    /// own delegates inline would hand a subscriber the audio thread.
    /// </remarks>
    void RaiseEvent(Action action);

    /// <summary>
    /// Nudges the decode thread, for when a channel suddenly needs audio it does not have — after a
    /// seek, or on starting playback.
    /// </summary>
    void WakeDecoder();
}
