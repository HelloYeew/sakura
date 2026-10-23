// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Platform;
using static SDL.SDL3;

namespace Sakura.Framework.iOS;

/// <summary>
/// The window an app draws to on iOS: SDL's, with the system's edge swipes set for the kind of app.
/// </summary>
internal class IOSWindow : SDLWindow
{
    private readonly AppType appType;

    public IOSWindow(AppType appType)
    {
        this.appType = appType;
    }

    protected override void ConfigureHints()
    {
        // A swipe in from an edge goes home, or opens Control Center or the notifications. A game asks for two of them,
        // the first only showing where it would go, so sliding off the bottom of a lane doesn't leave the game; the
        // home indicator stays, dimmed. An application leaves the edges to the system, as any other app does. Unset,
        // SDL would defer every edge for any borderless window, which on iOS is every window.
        SDL_SetHint(SDL_HINT_IOS_HIDE_HOME_INDICATOR, appType == AppType.Game ? "2" : "0");
    }
}
