// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Drawables;

namespace Sakura.Framework.Input;

/// <summary>
/// Sent to a drawable that has just taken focus.
/// </summary>
public readonly struct FocusEvent
{
    /// <summary>
    /// What moved focus here.
    /// </summary>
    public FocusSource Source { get; }

    /// <summary>
    /// Whether the drawable should show that it has focus, such as with a focus ring. The same as
    /// <see cref="Drawable.IsFocusVisible"/>, which says when it is true.
    /// </summary>
    public bool IsFocusVisible { get; }

    /// <summary>
    /// Creates the event for focus that <paramref name="source"/> moved.
    /// </summary>
    /// <param name="source">What moved focus.</param>
    /// <param name="isFocusVisible">Whether the drawable should show that it has focus.</param>
    public FocusEvent(FocusSource source, bool isFocusVisible)
    {
        Source = source;
        IsFocusVisible = isFocusVisible;
    }
}
