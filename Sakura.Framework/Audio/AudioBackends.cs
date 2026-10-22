// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ManagedBass;
using Sakura.Framework.Audio.BassEngine;
using Sakura.Framework.Audio.Headless;
using Sakura.Framework.Audio.SdlEngine;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Audio;

/// <summary>
/// Helpers for determining which <see cref="AudioBackend"/> values make sense to offer a user.
/// </summary>
public static class AudioBackends
{
    /// <summary>
    /// The backends a user can meaningfully select, in display order: BASS only where its library is there to load.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Managed <see cref="SDLAudioManager"/> still be selectable for testing selectable by writing <c>AudioBackend = SDLManaged</c> into <c>framework.ini</c>,
    /// but should be really use in worst case (e.g. libsakura-audio buggy, something really wrong) but most fallback still <see cref="BassAudioManager"/>
    /// </para>
    /// </remarks>
    public static IReadOnlyList<AudioBackend> GetSuitableBackends() => BassAvailable
        ? [AudioBackend.Automatic, AudioBackend.BASS, AudioBackend.SDL]
        : [AudioBackend.Automatic, AudioBackend.SDL];

    private static bool? bassAvailable;

    /// <summary>
    /// Whether BASS's library loads here. Android ships without it, and reaching for it there ends the app.
    /// </summary>
    public static bool BassAvailable => bassAvailable ??= NativeLibrary.TryLoad("bass", typeof(Bass).Assembly, null, out _);

    /// <summary>
    /// The audio manager to run with for <paramref name="backend"/>: BASS where it was asked for and loads, SDL
    /// otherwise, BASS again if SDL won't start, and silence if neither will, so a missing backend costs the sound and
    /// not the app.
    /// </summary>
    /// <param name="backend">What the settings ask for.</param>
    /// <param name="createSdl">Starts the SDL backend, throwing if it can't.</param>
    /// <param name="createBass">Starts the BASS backend.</param>
    /// <param name="bassAvailable">Whether BASS's library loads (<see cref="BassAvailable"/>).</param>
    internal static IAudioManager Create(AudioBackend backend, Func<IAudioManager> createSdl, Func<IAudioManager> createBass, bool bassAvailable)
    {
        if (backend == AudioBackend.BASS)
        {
            if (bassAvailable)
                return createBass();

            Logger.Warning("The settings ask for BASS, which isn't available here, so sound plays through SDL.");
        }

        try
        {
            return createSdl();
        }
        catch (Exception e)
        {
            Logger.Error(bassAvailable ? "The SDL audio backend wouldn't start, so sound plays through BASS." : "The SDL audio backend wouldn't start.", e);
        }

        if (bassAvailable)
            return createBass();

        Logger.Error("No audio backend would start, so the app runs without sound.");
        return new HeadlessAudioManager();
    }
}
