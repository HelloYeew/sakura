// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Platform;

/// <summary>
/// The kinds of feedback <see cref="IPlatformHaptics.Play"/> plays, as each platform's own feel for them.
/// </summary>
public enum HapticFeedback
{
    /// <summary>
    /// The lightest tick, as a picker or a list snaps to its next item.
    /// </summary>
    Selection,

    /// <summary>
    /// A light tap, as of a small thing landing.
    /// </summary>
    Light,

    /// <summary>
    /// A tap of middling strength.
    /// </summary>
    Medium,

    /// <summary>
    /// A strong tap, as of something heavy landing.
    /// </summary>
    Heavy,

    /// <summary>
    /// Something finished as it should: a save, a full combo.
    /// </summary>
    Success,

    /// <summary>
    /// Something needs attention.
    /// </summary>
    Warning,

    /// <summary>
    /// Something failed.
    /// </summary>
    Error
}
