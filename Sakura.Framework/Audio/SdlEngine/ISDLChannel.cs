// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics.CodeAnalysis;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// The part of an SDL-backend channel the backend itself needs, over and above
/// <see cref="IAudioChannel"/>: disposal notification, and a place to raise events that originated on
/// the audio thread.
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
internal interface ISDLChannel : IAudioChannel
{
    /// <summary>
    /// Raised once this channel has released its source. Whatever created the channel uses it to drop
    /// its reference, keeping the underlying audio data alive until then.
    /// </summary>
    event Action? Disposed;

    /// <summary>
    /// Raises anything the audio side has signaled since the last call, on the update thread.
    /// </summary>
    /// <remarks>
    /// Only the native engine needs this since its audio thread cannot raise a managed event, so an ended
    /// or looped voice publishes a counter that this reads. The managed mixer marshals its own events
    /// through <see cref="ISDLAudioContext.EnqueueAction"/> instead, and does nothing here.
    /// </remarks>
    void PollEvents();
}

/// <summary>
/// An SDL-backend mixer, over and above <see cref="IAudioMixer"/>.
/// </summary>
/// <remarks>
/// Exists for <see cref="RunningChannelCount"/>, which the manager reports as a statistic and which
/// the managed mix loop uses to tell a dry device from an idle one. Not on
/// <see cref="IAudioMixer"/> because it is a diagnostic, not something an app should route audio by.
/// </remarks>
[SuppressMessage("ReSharper", "InconsistentNaming")]
internal interface ISDLMixer : IAudioMixer
{
    /// <summary>
    /// The number of children currently producing audio.
    /// </summary>
    int RunningChannelCount { get; }
}
