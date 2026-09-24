// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Input;

/// <summary>
/// Implemented by a drawable that wants the mouse cursor to take a particular shape while it is
/// under the pointer — a pointer over something clickable, an I-beam over text, a closed hand-over
/// something being dragged.
/// </summary>
public interface IHasCursor
{
    /// <summary>
    /// The shape this drawable wants while it is the front-most thing under the pointer. Read every
    /// frame, so it may vary with the drawable's own state — enabled, dragged, or a mode it is in.
    /// </summary>
    CursorState Cursor { get; }
}
