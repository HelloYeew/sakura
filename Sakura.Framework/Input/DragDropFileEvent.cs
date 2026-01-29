// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Maths;

namespace Sakura.Framework.Input;

public readonly struct DragDropFileEvent
{
    /// <summary>
    /// The full path to the file that was dropped.
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// The screen-space position of the mouse cursor when the drop occurred.
    /// </summary>
    public Vector2 Position { get; }

    public DragDropFileEvent(string filePath, Vector2 position)
    {
        FilePath = filePath;
        Position = position;
    }
}
