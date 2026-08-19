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
    /// Queues work for the audio thread. Channel state changes and user-facing events go through
    /// here rather than firing on the mix thread, matching the BASS backend.
    /// </summary>
    void EnqueueAction(Action action);

    /// <summary>
    /// Nudges the decode thread, for when a channel suddenly needs audio it does not have — after a
    /// seek, or on starting playback.
    /// </summary>
    void WakeDecoder();
}
