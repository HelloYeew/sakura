// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Platform;

/// <summary>
/// What a game is doing, for the system to plan around. See <see cref="IPlatformPerformance.SetGameState"/>.
/// </summary>
public enum GameState
{
    /// <summary>
    /// Nothing in particular, such as menus or results.
    /// </summary>
    None,

    /// <summary>
    /// Play the player can pause at any moment, such as a single-player song.
    /// </summary>
    Gameplay,

    /// <summary>
    /// Play that mustn't be interrupted, such as a live online match.
    /// </summary>
    UninterruptibleGameplay,

    /// <summary>
    /// Something to watch rather than play, such as a replay or a cutscene.
    /// </summary>
    Content
}
