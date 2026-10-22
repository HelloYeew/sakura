// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Android.Text;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Platform;
using SDL;
using static SDL.SDL3;

namespace Sakura.Framework.Android;

/// <summary>
/// The window an app draws to on Android: SDL's, with the safe area taken from what covers the content.
/// </summary>
internal class AndroidWindow : SDLWindow
{
    private readonly SakuraActivity activity;

    public AndroidWindow(SakuraActivity activity, AppType appType)
    {
        this.activity = activity;
        activity.CoveredInsetsChanged += InvalidateSafeArea;
        activity.RefreshRateChanged += InvalidateRefreshRate;

        // A game wants every frame the panel can show. An application leaves the rate to the system, which can drop
        // it while nothing moves to save power.
        if (appType == AppType.Game)
            activity.PreferHighestRefreshRate();
    }

    protected override MarginPadding? GetPlatformSafeAreaInsets() => activity.CoveredInsets;

    protected override float? GetPlatformRefreshRate() => activity.RefreshRate > 0 ? activity.RefreshRate : null;

    protected override void ConfigureHints()
    {
        // Back comes to the app as a key (Key.SystemBack) rather than straight to the system, so the app can
        // close a menu or pause first. The host hands an unhandled one back to the system.
        SDL_SetHint(SDL_HINT_ANDROID_TRAP_BACK_BUTTON, "1");

        // The input loop sleeps in SDL_WaitEventTimeout, which on Android pumps events, moving the poll sentinel to
        // the end of the queue, and then waits on a semaphore that every queued event signals, the sentinel too.
        // So it woke itself at once, forever: a whole core spun. Without the sentinel, a poll drains the queue
        // instead of stopping after one pass, which costs nothing at the rate a phone's events come in.
        SDL_SetHint(SDL_HINT_POLL_SENTINEL, "0");

        // AAudio's low-latency mode, the path a burst-sized buffer at the output's rate is for. SDL's default
        // today; set so a change of default can't quietly take it away.
        SDL_SetHint(SDL_HINT_ANDROID_LOW_LATENCY_AUDIO, "1");
    }

    protected override bool RetargetScreenKeyboard(TextInputType type, SDL_Rect area, bool typeChanged)
    {
        activity.RetargetKeyboard(inputTypeFor(type), area, typeChanged);
        return true;
    }

    /// <summary>
    /// What SDL opens the keyboard with for <paramref name="type"/>, its default capitalisation, autocorrection and
    /// multi-line flags included, so a keyboard moved onto a field matches one opened for it.
    /// </summary>
    private static InputTypes inputTypeFor(TextInputType type)
    {
        const InputTypes defaults = InputTypes.TextFlagAutoCorrect | InputTypes.TextFlagAutoComplete | InputTypes.TextFlagMultiLine;

        return defaults | type switch
        {
            TextInputType.Name => InputTypes.ClassText | InputTypes.TextVariationPersonName | InputTypes.TextFlagCapWords,
            TextInputType.Email => InputTypes.ClassText | InputTypes.TextVariationEmailAddress,
            TextInputType.Username => InputTypes.ClassText | InputTypes.TextVariationNormal,
            TextInputType.Password => InputTypes.ClassText | InputTypes.TextVariationPassword,
            TextInputType.Number => InputTypes.ClassNumber | InputTypes.NumberVariationNormal,
            TextInputType.NumberPassword => InputTypes.ClassNumber | InputTypes.NumberVariationPassword,
            _ => InputTypes.ClassText | InputTypes.TextVariationNormal | InputTypes.TextFlagCapSentences
        };
    }
}
