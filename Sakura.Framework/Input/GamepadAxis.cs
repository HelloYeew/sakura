// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Input;

/// <summary>
/// Axes on a gamepad/controller, mapped from SDL_GamepadAxis.
/// All axis values are normalised to the range [-1, 1].
/// Trigger axes (LeftTrigger, RightTrigger) range from 0 (released) to 1 (fully pressed).
/// </summary>
public enum GamepadAxis
{
    Unknown = -1,

    /// <summary>
    /// Left stick horizontal. Negative = left, positive = right.
    /// </summary>
    LeftX = 0,

    /// <summary>
    /// Left stick vertical. Negative = up, positive = down.
    /// </summary>
    LeftY = 1,

    /// <summary>
    /// Right stick horizontal. Negative = left, positive = right.
    /// </summary>
    RightX = 2,

    /// <summary>
    /// Right stick vertical. Negative = up, positive = down.
    /// </summary>
    RightY = 3,

    /// <summary>
    /// Left trigger (LT / L2). Range [0, 1].
    /// </summary>
    LeftTrigger = 4,

    /// <summary>
    /// Right trigger (RT / R2). Range [0, 1].
    /// </summary>
    RightTrigger = 5,
}
