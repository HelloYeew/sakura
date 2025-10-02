// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Input;

public class MouseState
{
    public Vector2 Position { get; set; }

    private readonly HashSet<MouseButton> pressedButtons = new HashSet<MouseButton>();

    public bool IsPressed(MouseButton button) => pressedButtons.Contains(button);

    public void SetPressed(MouseButton button, bool pressed)
    {
        if (pressed)
            pressedButtons.Add(button);
        else
            pressedButtons.Remove(button);
    }
}
