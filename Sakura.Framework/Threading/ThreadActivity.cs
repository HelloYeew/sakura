// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Threading;

/// <summary>
/// How much the host's threads ask the system for: how high their priority is, how precisely they wait, and how fast
/// the cores they run on are. The host works it out from the window and from what the app says it's doing, and each
/// thread applies it to itself between frames.
/// </summary>
public enum ThreadActivity
{
    /// <summary>
    /// A focused game in play: on time and at full speed. The frame threads go ahead of other apps' work, wait with the
    /// most precise timer there is, may spin onto their deadlines, and ask for fast cores.
    /// </summary>
    Tight,

    /// <summary>
    /// Focused, but nothing needs sub-millisecond timing: a game's menus, or an application. Still ahead of background
    /// load, but with ordinary waits, no spinning, and efficient cores when they keep up.
    /// </summary>
    Smooth,

    /// <summary>
    /// On screen but unfocused: makes way for the app the user is in.
    /// </summary>
    Background,

    /// <summary>
    /// Can't be seen at all: minimized, covered by other windows, or a phone app in the background. Asks for as little
    /// as possible.
    /// </summary>
    Hidden
}
