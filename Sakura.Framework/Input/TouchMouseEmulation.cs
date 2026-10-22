// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Input;

/// <summary>
/// Turns a gesture's first finger, when nothing took it (<see cref="Drawable.OnTouchDown"/>), into mouse input for
/// UI that only handles the mouse, the way a touchscreen behaves:
/// <list type="bullet">
/// <item>A tap is a click when the finger lifts, not when it lands, so a finger that goes on to scroll presses nothing.</item>
/// <item>A finger that moves past <see cref="SLOP"/> drags the scroll container under it that scrolls that way,
/// rather than pressing what it landed on. With no such container it presses where it landed and drags from there,
/// as a slider wants.</item>
/// <item>Once the finger lifts, nothing stays hovered: there is no pointer left (<see cref="MouseState.FromTouch"/>).</item>
/// </list>
/// </summary>
internal sealed class TouchMouseEmulation
{
    /// <summary>
    /// How far a finger can wander, in screen space, and still be a tap.
    /// </summary>
    public const float SLOP = 10;

    private const double double_tap_milliseconds = 300;
    private const float double_tap_distance = 20;

    private readonly Drawable root;
    private readonly InputManager manager;
    private readonly Func<MouseEvent, bool> mouseMove;
    private readonly Func<MouseButtonEvent, bool> mouseDown;
    private readonly Func<MouseButtonEvent, bool> mouseUp;

    /// <summary>
    /// Whether a finger stands in for the mouse right now.
    /// </summary>
    private bool active;

    /// <summary>
    /// Whether the emulated button is down: the finger moved past the slop.
    /// </summary>
    private bool pressed;

    private TouchSource source;
    private Vector2 start;
    private Vector2 position;

    private double lastTapTimestamp = double.NaN;
    private Vector2 lastTapPosition;
    private int tapCount;

    /// <param name="root">The root the manager's queues are built from.</param>
    /// <param name="manager">The manager the root dispatches with.</param>
    /// <param name="mouseMove">Takes an emulated mouse move the way the root takes a real one.</param>
    /// <param name="mouseDown">Takes an emulated button press the way the root takes a real one.</param>
    /// <param name="mouseUp">Takes an emulated button release the way the root takes a real one.</param>
    public TouchMouseEmulation(Drawable root, InputManager manager, Func<MouseEvent, bool> mouseMove, Func<MouseButtonEvent, bool> mouseDown,
                               Func<MouseButtonEvent, bool> mouseUp)
    {
        this.root = root;
        this.manager = manager;
        this.mouseMove = mouseMove;
        this.mouseDown = mouseDown;
        this.mouseUp = mouseUp;
    }

    /// <summary>
    /// A finger went down that nothing took. Call after the manager has recorded it.
    /// </summary>
    public void TouchDown(TouchEvent e)
    {
        // Only a gesture's first finger, while no other is down; a second finger is a different gesture.
        if (active || manager.CurrentState.ActiveTouchCount != 1)
            return;

        active = true;
        pressed = false;
        source = e.Source;
        start = position = e.ScreenSpacePosition;
    }

    public void TouchMove(TouchEvent e)
    {
        if (!active || e.Source != source)
            return;

        if (!pressed)
        {
            Vector2 travelled = e.ScreenSpacePosition - start;

            if (travelled.LengthSquared() < SLOP * SLOP)
                return;

            press(1, scrollContainerAlong(travelled), e.Timestamp);
        }

        moveTo(e.ScreenSpacePosition, e.Timestamp);
    }

    public void TouchUp(TouchEvent e)
    {
        if (!active || e.Source != source)
            return;

        active = false;

        if (pressed)
        {
            moveTo(e.ScreenSpacePosition, e.Timestamp);
            release(1, e.Timestamp);
        }
        else if (!e.Cancelled)
            tap(e.Timestamp);
    }

    private void tap(double timestamp)
    {
        bool again = !double.IsNaN(timestamp)
                     && timestamp - lastTapTimestamp <= double_tap_milliseconds
                     && (start - lastTapPosition).LengthSquared() <= double_tap_distance * double_tap_distance;

        tapCount = again ? tapCount + 1 : 1;
        lastTapTimestamp = timestamp;
        lastTapPosition = start;

        press(tapCount, null, timestamp);
        release(tapCount, timestamp);
    }

    /// <summary>
    /// The nearest scroll container under where the finger landed that a drag along <paramref name="direction"/>
    /// would scroll, or null.
    /// </summary>
    private ScrollableContainer? scrollContainerAlong(Vector2 direction)
    {
        manager.BuildQueues(root, start);
        var queue = manager.PositionalInputQueue;

        for (int i = 0; i < queue.Count; i++)
        {
            if (queue[i] is ScrollableContainer scroll && scroll.ScrollsAlong(direction))
                return scroll;
        }

        return null;
    }

    /// <summary>
    /// Presses the emulated button where the finger landed, for <paramref name="onlyTarget"/> alone when given.
    /// </summary>
    private void press(int clicks, Drawable? onlyTarget, double timestamp)
    {
        moveTo(start, timestamp);

        pressed = true;
        mouseDown(new MouseButtonEvent(mouseState(), MouseButton.Left, clicks, timestamp, onlyTarget));
    }

    private void release(int clicks, double timestamp)
    {
        pressed = false;
        mouseUp(new MouseButtonEvent(mouseState(), MouseButton.Left, clicks, timestamp));
    }

    private void moveTo(Vector2 to, double timestamp)
    {
        Vector2 delta = to - position;
        position = to;
        mouseMove(new MouseEvent(mouseState(), delta, timestamp));
    }

    private MouseState mouseState()
    {
        var state = new MouseState { Position = position, FromTouch = true };
        state.SetPressed(MouseButton.Left, pressed);
        return state;
    }
}
