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

    public AndroidWindow(SakuraActivity activity)
    {
        this.activity = activity;
        activity.CoveredInsetsChanged += InvalidateSafeArea;
    }

    protected override MarginPadding? GetPlatformSafeAreaInsets() => activity.CoveredInsets;

    protected override void ConfigureHints()
    {
        // Back comes to the app as a key (Key.SystemBack) rather than straight to the system, so the app can
        // close a menu or pause first. The host hands an unhandled one back to the system.
        SDL_SetHint(SDL_HINT_ANDROID_TRAP_BACK_BUTTON, "1");
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
