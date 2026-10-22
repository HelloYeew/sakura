// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Platform;

/// <summary>
/// How fast the host's loops run. A game wants the most frames and the quickest response, and lets its player
/// change the frame limiter; an application only has to run smoothly. On a phone, frames the panel can't show and
/// loops faster than anything changes cost battery and heat, which then costs frames as the phone throttles.
/// </summary>
/// <param name="AppType">What kind of app this is.</param>
/// <param name="Mobile">Whether it runs on a phone or tablet.</param>
internal readonly record struct FramePacing(AppType AppType, bool Mobile)
{
    /// <summary>
    /// The fastest anything updates on a phone, whatever the frame limiter says.
    /// </summary>
    public const double MOBILE_MAX_UPDATE_HZ = 240;

    /// <summary>
    /// The fastest anything updates or polls on desktop.
    /// </summary>
    public const double DESKTOP_MAX_HZ = 1000;

    /// <summary>
    /// The audio thread's rate for a game on a phone whose audio backend only starts a sound at the thread's next
    /// tick: up to 4 ms late, well inside a frame.
    /// </summary>
    public const double MOBILE_GAME_AUDIO_HZ = 250;

    public static FramePacing Current(AppType appType) => new FramePacing(appType, RuntimeInfo.IsMobile);

    /// <summary>
    /// The frame limiter an install starts with. A game on desktop draws at twice the display's rate, for the
    /// freshest frame at each refresh. Elsewhere frames beyond the display's rate are only thrown away.
    /// </summary>
    public FrameSync DefaultFrameLimiter => AppType == AppType.Game && !Mobile ? FrameSync.Limit2x : FrameSync.VSync;

    /// <summary>
    /// How fast the update thread runs for a draw rate.
    /// </summary>
    /// <param name="drawHz">The draw rate, or 0 for unlimited.</param>
    /// <param name="limitUnlimited">Desktop only: whether an unlimited rate still stops at <see cref="DESKTOP_MAX_HZ"/>.</param>
    /// <returns>The rate, or 0 for unlimited.</returns>
    public double UpdateHz(double drawHz, bool limitUnlimited)
    {
        // A desktop game updates twice between drawn frames, for input and physics precision. On a phone input
        // is judged by its timestamps anyway, and an update only shows once it's drawn, so updates follow draws.
        if (Mobile)
            return drawHz > 0 ? Math.Min(drawHz, MOBILE_MAX_UPDATE_HZ) : MOBILE_MAX_UPDATE_HZ;

        if (drawHz == 0)
            return limitUnlimited ? DESKTOP_MAX_HZ : 0;

        double updateHz = AppType == AppType.Game ? drawHz * 2 : drawHz;

        return limitUnlimited ? Math.Min(updateHz, DESKTOP_MAX_HZ) : updateHz;
    }

    /// <summary>
    /// How often the input loop runs when it has the SDL thread to itself, or at most how long it waits for an
    /// event when it <see cref="InputWaitsForEvents"/>.
    /// </summary>
    public double InputHz(double displayHz) => AppType == AppType.Game && !Mobile ? DESKTOP_MAX_HZ : displayHz;

    /// <summary>
    /// How fast the audio thread advances audio state. An application polls no faster than it can show the
    /// result: nobody hears a click sound start a frame late.
    /// </summary>
    /// <param name="displayHz">The display's refresh rate.</param>
    /// <param name="playbackWaitsForUpdate">Whether the audio backend starts a sound only at the thread's next
    /// tick (<see cref="Audio.IAudioManager.PlaybackWaitsForUpdate"/>).</param>
    public double AudioHz(double displayHz, bool playbackWaitsForUpdate)
    {
        if (AppType != AppType.Game)
            return displayHz;

        if (!Mobile)
            return DESKTOP_MAX_HZ;

        // A mixer that takes playback straight from the game leaves this thread only end-of-sound events and
        // tidying up, which a frame's delay doesn't change.
        return playbackWaitsForUpdate ? MOBILE_GAME_AUDIO_HZ : displayHz;
    }

    /// <summary>
    /// Whether the input loop, on a thread of its own, sleeps until an event arrives rather than polling. An event
    /// wakes it at once, so input is no later, and with nothing happening it wakes once a frame instead of a
    /// thousand times a second.
    /// </summary>
    public bool InputWaitsForEvents => Mobile;
}
