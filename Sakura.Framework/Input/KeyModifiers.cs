// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Input;

/// <summary>
/// Enumerates modifier keys.
/// </summary>
[Flags]
public enum KeyModifiers : byte
{
    /// <summary>
    /// No modifier key is pressed.
    /// </summary>
    None = 0,

    /// <summary>
    /// The alt key modifier (option on Mac).
    /// </summary>
    Alt = 1 << 0,

    /// <summary>
    /// The control key modifier.
    /// </summary>
    Control = 1 << 1,

    /// <summary>
    /// The shift key modifier.
    /// </summary>
    Shift = 1 << 2
}
