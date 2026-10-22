// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Platform;
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
}
