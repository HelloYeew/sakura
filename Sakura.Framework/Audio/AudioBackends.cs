// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using Sakura.Framework.Audio.BassEngine;
using Sakura.Framework.Audio.SdlEngine;

namespace Sakura.Framework.Audio;

/// <summary>
/// Helpers for determining which <see cref="AudioBackend"/> values make sense to offer a user.
/// </summary>
public static class AudioBackends
{
    /// <summary>
    /// The backends a user can meaningfully select, in display order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Managed <see cref="SDLAudioManager"/> still be selectable for testing selectable by writing <c>AudioBackend = SDLManaged</c> into <c>framework.ini</c>,
    /// but should be really use in worst case (e.g. libsakura-audio buggy, something really wrong) but most fallback still <see cref="BassAudioManager"/>
    /// </para>
    /// </remarks>
    public static IReadOnlyList<AudioBackend> GetSuitableBackends() =>
    [
        AudioBackend.Automatic,
        AudioBackend.BASS,
        AudioBackend.SDL
    ];
}
