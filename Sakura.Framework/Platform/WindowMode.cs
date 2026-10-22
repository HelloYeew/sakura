// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Platform;

public enum WindowMode
{
    /// <summary>
    /// The window is in a windowed mode with borders and title bar.
    /// On a phone, the app fills the screen with the system bars shown over it.
    /// </summary>
    Windowed,

    /// <summary>
    /// The window is borderless and occupies the entire screen without title bar.
    /// Often refer as "fake fullscreen".
    /// On a phone, the system bars are hidden, and a swipe from the edge shows them for a moment (immersive mode).
    /// </summary>
    Borderless,

    /// <summary>
    /// The window is in exclusive fullscreen mode, taking over the entire display.
    /// Desktop only: a phone has no display mode of its own to take, and treats it as <see cref="Borderless"/>.
    /// </summary>
    Fullscreen
}
