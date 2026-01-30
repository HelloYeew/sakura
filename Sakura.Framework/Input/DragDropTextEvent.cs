// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Maths;

namespace Sakura.Framework.Input;

public readonly struct DragDropTextEvent
{
    /// <summary>
    /// The text that was dropped.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// The screen-space position of the mouse cursor when the drop occurred.
    /// </summary>
    public Vector2 Position { get; }

    public DragDropTextEvent(string text, Vector2 position)
    {
        Text = text;
        Position = position;
    }
}
