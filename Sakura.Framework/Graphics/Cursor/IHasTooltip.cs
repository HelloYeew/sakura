// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Drawables;

namespace Sakura.Framework.Graphics.Cursor;

/// <summary>
/// Implement on any <see cref="Drawable"/> to make <see cref="TooltipContainer"/> display a
/// tooltip when the cursor hovers over it long enough.
/// </summary>
public interface IHasTooltip
{
    /// <summary>
    /// The text to display inside the tooltip. Return null or empty to suppress the tooltip.
    /// </summary>
    string? TooltipText { get; }
}
