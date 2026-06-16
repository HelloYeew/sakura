// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Input;

/// <summary>
/// Buttons on a gamepad/controller, mapped from SDL_GamepadButton.
/// </summary>
public enum GamepadButton
{
    Unknown = -1,

    /// <summary>
    /// Bottom face button (A on Xbox, Cross on PlayStation)
    /// </summary>
    South = 0,

    /// <summary>
    /// Right face button (B on Xbox, Circle on PlayStation)
    /// </summary>
    East = 1,

    /// <summary>
    /// Left face button (X on Xbox, Square on PlayStation)
    /// </summary>
    West = 2,

    /// <summary>
    /// Top face button (Y on Xbox, Triangle on PlayStation)
    /// </summary>
    North = 3,

    /// <summary>
    /// Back / Select button
    /// </summary>
    Back = 4,

    /// <summary>
    /// Guide / Home / Xbox button
    /// </summary>
    Guide = 5,

    /// <summary>
    /// Start button
    /// </summary>
    Start = 6,

    /// <summary>
    /// Left stick click (L3)
    /// </summary>
    LeftStick = 7,

    /// <summary>
    /// Right stick click (R3)
    /// </summary>
    RightStick = 8,

    /// <summary>
    /// Left shoulder button (LB / L1)
    /// </summary>
    LeftShoulder = 9,

    /// <summary>
    /// Right shoulder button (RB / R1)
    /// </summary>
    RightShoulder = 10,

    /// <summary>
    /// D-pad up
    /// </summary>
    DPadUp = 11,

    /// <summary>
    /// D-pad down
    /// </summary>
    DPadDown = 12,

    /// <summary>
    /// D-pad left
    /// </summary>
    DPadLeft = 13,

    /// <summary>
    /// D-pad right
    /// </summary>
    DPadRight = 14,

    /// <summary>
    /// Extra button 1 (e.g. Share on DualSense)
    /// </summary>
    Misc1 = 15,

    /// <summary>
    /// Right paddle 1 (Xbox Elite)
    /// </summary>
    RightPaddle1 = 16,

    /// <summary>
    /// Left paddle 1 (Xbox Elite)
    /// </summary>
    LeftPaddle1 = 17,

    /// <summary>
    /// Right paddle 2 (Xbox Elite)
    /// </summary>
    RightPaddle2 = 18,

    /// <summary>
    /// Left paddle 2 (Xbox Elite)
    /// </summary>
    LeftPaddle2 = 19,

    /// <summary>
    /// Touchpad click (DualShock 4 / DualSense)
    /// </summary>
    Touchpad = 20,

    /// <summary>
    /// Extra button 2.
    /// </summary>
    Misc2 = 21,

    /// <summary>
    /// Extra button 3.
    /// </summary>
    Misc3 = 22,

    /// <summary>
    /// Extra button 4.
    /// </summary>
    Misc4 = 23,

    /// <summary>
    /// Extra button 5.
    /// </summary>
    Misc5 = 24,

    /// <summary>
    /// Extra button 6.
    /// </summary>
    Misc6 = 25,
}
