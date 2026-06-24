// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Input.Bindings;

/// <summary>
/// Controls how many key bindings may be active (pressed) at the same time within a
/// <see cref="KeyBindingContainer{T}"/>.
/// </summary>
public enum SimultaneousBindingMode
{
    /// <summary>
    /// Only one binding may be active at a time. When a new combination becomes satisfied, any
    /// currently-active binding is released first. This is the default and is suited to
    /// menu-style navigation where only the latest action should apply.
    /// </summary>
    None,

    /// <summary>
    /// Multiple bindings may be active simultaneously, but the same <em>action</em> will not be
    /// pressed more than once at a time. Releasing one of several keys mapped to the same action
    /// keeps the action pressed until the last is released.
    /// </summary>
    Unique,

    /// <summary>
    /// Multiple bindings may be active simultaneously, and a single action may be pressed multiple
    /// times concurrently (once per satisfied binding). Suited to rhythm-game-style input where
    /// each physical key mapped to an action is tracked independently.
    /// </summary>
    All,
}
