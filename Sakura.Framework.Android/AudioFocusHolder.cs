// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Android.Content;
using Android.Media;
using Sakura.Framework.Platform;
using AndroidAudioManager = Android.Media.AudioManager;

namespace Sakura.Framework.Android;

/// <summary>
/// Holds Android's audio focus while the app is on screen: another app's music pauses for it, and the app's own sound
/// waits out whatever takes the focus for a while, such as a call or an alarm.
/// </summary>
internal sealed class AudioFocusHolder : Java.Lang.Object, AndroidAudioManager.IOnAudioFocusChangeListener
{
    private readonly AndroidAudioManager audioManager;
    private readonly AudioFocusRequestClass request;
    private readonly Action<bool> setInterrupted;

    /// <param name="context">The app's context.</param>
    /// <param name="appType">What the sound is for, which the request says the same way SDL's stream does.</param>
    /// <param name="setInterrupted">Told whether something else has the output for now; called on the UI thread or the caller's.</param>
    public AudioFocusHolder(Context context, AppType appType, Action<bool> setInterrupted)
    {
        audioManager = (AndroidAudioManager)context.GetSystemService(Context.AudioService)!;
        this.setInterrupted = setInterrupted;

        var attributes = new AudioAttributes.Builder()
                         .SetUsage(appType == AppType.Game ? AudioUsageKind.Game : AudioUsageKind.Media)!
                         .SetContentType(AudioContentType.Music)!
                         .Build()!;

        request = new AudioFocusRequestClass.Builder(AudioFocus.Gain)
                  .SetAudioAttributes(attributes)
                  // Asked for during a call, the focus comes once the call ends rather than never.
                  .SetAcceptsDelayedFocusGain(true)
                  .SetOnAudioFocusChangeListener(this)
                  .Build()!;
    }

    /// <summary>
    /// Takes the focus. Asking again while holding it changes nothing, so this is called every time the app comes to
    /// the front, which takes the focus back from an app that took it for good meanwhile.
    /// </summary>
    public void Take()
    {
        var result = audioManager.RequestAudioFocus(request);

        // Failed only where nothing will ever give it: playing over whatever has it beats staying silent.
        setInterrupted(result == AudioFocusRequest.Delayed);
    }

    /// <summary>
    /// Gives the focus up, for an app that's no longer on screen.
    /// </summary>
    public void Release() => audioManager.AbandonAudioFocusRequest(request);

    public void OnAudioFocusChange(AudioFocus focusChange)
    {
        switch (focusChange)
        {
            case AudioFocus.Gain:
                setInterrupted(false);
                break;

            // For good (another app's music) or for now (a call, an alarm): either way, quiet until it's back.
            case AudioFocus.Loss:
            case AudioFocus.LossTransient:
                setInterrupted(true);
                break;

            // Something brief over the top, such as a navigation prompt: Android lowers the app's volume itself.
            case AudioFocus.LossTransientCanDuck:
                break;
        }
    }
}
