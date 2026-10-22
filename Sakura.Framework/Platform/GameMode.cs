// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Platform;

/// <summary>
/// The mode a player picks for a game in the system's game settings. See <see cref="IPlatformPerformance.GameMode"/>.
/// </summary>
public enum GameMode
{
    /// <summary>
    /// No preference, or one the game isn't told the details of.
    /// </summary>
    Standard,

    /// <summary>
    /// The most frames, at the battery's expense.
    /// </summary>
    Performance,

    /// <summary>
    /// A longer battery life, at the cost of frames and detail.
    /// </summary>
    Battery
}
