// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Input;

public class MouseState
{
    public Vector2 Position { get; set; }

    /// <summary>
    /// Whether a finger is standing in for the mouse (<see cref="TouchMouseEmulation"/>). There is no real pointer
    /// then: once the finger lifts nothing stays hovered, and no cursor shows.
    /// </summary>
    public bool FromTouch { get; set; }

    /// <summary>
    /// One bit per <see cref="MouseButton"/>. Every mouse event carries a copy of the state, and a set would be an
    /// allocation of its own in each one.
    /// </summary>
    private int pressedButtons;

    private static int bit(MouseButton button) => (uint)button < 32 ? 1 << (int)button : 0;

    public bool IsPressed(MouseButton button) => (pressedButtons & bit(button)) != 0;

    public MouseState Clone() => new MouseState
    {
        Position = Position,
        FromTouch = FromTouch,
        pressedButtons = pressedButtons
    };

    public void SetPressed(MouseButton button, bool pressed)
    {
        if (pressed)
            pressedButtons |= bit(button);
        else
            pressedButtons &= ~bit(button);
    }

    public override string ToString()
    {
        var pressed = new List<MouseButton>();

        foreach (var button in Enum.GetValues<MouseButton>())
        {
            if (IsPressed(button))
                pressed.Add(button);
        }

        return $"MouseState: Position={Position}, PressedButtons=[{string.Join(", ", pressed)}]";
    }
}
