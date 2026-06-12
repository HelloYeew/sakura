// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Cursor;

/// <summary>
/// Contract for a tooltip drawable managed by <see cref="TooltipContainer"/>.
/// </summary>
public interface ITooltip
{
    /// <summary>
    /// Update the content displayed by this tooltip.
    /// </summary>
    void SetContent(string content);

    /// <summary>
    /// Move the tooltip to a new position (local space of the <see cref="TooltipContainer"/>).
    /// Implementations may animate this movement.
    /// </summary>
    void Move(Vector2 position);

    /// <summary>
    /// Show this tooltip.
    /// </summary>
    void Show();

    /// <summary>
    /// Hide this tooltip.
    /// </summary>
    void Hide();
}
