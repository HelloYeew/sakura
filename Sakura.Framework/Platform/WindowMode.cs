// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Platform;

public enum WindowMode
{
    /// <summary>
    /// The window is in a windowed mode with borders and title bar.
    /// </summary>
    Windowed,

    /// <summary>
    /// The window is borderless and occupies the entire screen without title bar.
    /// Often refer as "fake fullscreen".
    /// </summary>
    Borderless,

    /// <summary>
    /// The window is in exclusive fullscreen mode, taking over the entire display.
    /// </summary>
    Fullscreen
}
