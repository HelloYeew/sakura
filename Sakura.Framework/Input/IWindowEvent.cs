// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Maths;

namespace Sakura.Framework.Input;

/// <summary>
/// Represent events that are related to window.
/// </summary>
public interface IWindowEvent
{
    /// <summary>
    /// The current size of the window.
    /// </summary>
    Vector2 WindowSize { get; }
}
