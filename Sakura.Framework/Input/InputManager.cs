// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Input;

/// <summary>
/// Owns a single authoritative <see cref="InputState"/> and builds two explicit, inspectable input
/// queues each frame
/// </summary>
public class InputManager
{
    /// <summary>
    /// The authoritative input snapshot. Mutated only by this manager.
    /// </summary>
    public InputState CurrentState { get; } = new InputState();

    private readonly List<Drawable> nonPositionalQueue = new List<Drawable>();
    private readonly List<Drawable> positionalQueue = new List<Drawable>();

    /// <summary>
    /// The most recently built non-positional input queue (front-to-back). Recomputed by
    /// <see cref="BuildQueues"/>. Exposed for inspection and testing; not yet used for dispatch.
    /// </summary>
    public IReadOnlyList<Drawable> NonPositionalInputQueue => nonPositionalQueue;

    /// <summary>
    /// The most recently built positional input queue (front-to-back) for the point passed to
    /// <see cref="BuildQueues"/>. Exposed for inspection and testing; not yet used for dispatch.
    /// </summary>
    public IReadOnlyList<Drawable> PositionalInputQueue => positionalQueue;

    /// <summary>
    /// Rebuilds both queues by walking <paramref name="root"/> depth-first, front-to-back. The
    /// non-positional queue includes every drawable opted in via
    /// <see cref="Drawable.HandleNonPositionalInput"/>; the positional queue additionally filters by
    /// <see cref="Drawable.HandlePositionalInput"/> and
    /// <see cref="Drawable.ReceivePositionalInputAt"/> at <paramref name="positionalPoint"/>.
    /// </summary>
    /// <param name="root">The subtree root to walk (typically the app root).</param>
    /// <param name="positionalPoint">The screen-space point to build the positional queue for.</param>
    public void BuildQueues(Drawable root, Vector2 positionalPoint)
    {
        nonPositionalQueue.Clear();
        positionalQueue.Clear();

        if (root == null)
            return;

        buildNonPositional(root);
        buildPositional(root, positionalPoint);
    }

    /// <summary>
    /// Rebuilds the queues using the current mouse position from <see cref="CurrentState"/>.
    /// </summary>
    public void BuildQueues(Drawable root) => BuildQueues(root, CurrentState.MousePosition);

    private void buildNonPositional(Drawable drawable)
    {
        if (!drawable.IsLoaded || !drawable.HandleNonPositionalInput)
            return;

        if (drawable is Container container)
        {
            var children = container.Children;
            for (int i = 0; i < children.Count; i++)
                buildNonPositional(children[i]);
        }

        nonPositionalQueue.Add(drawable);
    }

    private void buildPositional(Drawable drawable, Vector2 point)
    {
        if (!drawable.IsLoaded || !drawable.IsAlive || drawable.IsHidden || !drawable.HandlePositionalInput)
            return;

        bool receives = drawable.ReceivePositionalInputAt(point);

        // Only descend into a container the cursor is actually over, mirroring the recursive path
        // where a child container's OnMouseDown only fires (and thus recurses) when it Contains the
        // point. A full-screen root receives everywhere, so its children are always considered.
        if (receives && drawable is Container container)
        {
            var sorted = container.SortedChildren;
            for (int i = sorted.Count - 1; i >= 0; i--)
                buildPositional(sorted[i], point);
        }

        // Add after children so the front-most receiver sits at the front of the queue.
        if (receives)
            positionalQueue.Add(drawable);
    }

    /// <summary>
    /// The queue entry that consumed the most recent non-positional event (keyboard / text /
    /// gamepad), or <c>null</c> if the last event was unhandled. Exposed for the debug overlay so it
    /// can highlight "this is where the key went".
    /// </summary>
    public Drawable LastNonPositionalHandler { get; private set; }

    #region Non-positional dispatch

    /// <summary>
    /// Dispatches a non-positional event down the current <see cref="NonPositionalInputQueue"/>
    /// (front-to-back), invoking <paramref name="invoke"/> on each entry until one returns
    /// <c>true</c>. Records the consuming entry in <see cref="LastNonPositionalHandler"/>.
    /// </summary>
    /// <param name="invoke">Calls the appropriate <c>Trigger*</c> self-handler on a queue entry.</param>
    /// <returns><c>true</c> if a queue entry handled the event.</returns>
    private bool dispatchNonPositional(System.Func<Drawable, bool> invoke)
    {
        // Snapshot count up front; handlers may mutate the tree, and entries are skipped if they
        // were removed/unloaded mid-dispatch (mirroring the guards on the old recursive path).
        for (int i = 0; i < nonPositionalQueue.Count; i++)
        {
            var drawable = nonPositionalQueue[i];

            if (!drawable.IsLoaded)
                continue;

            if (invoke(drawable))
            {
                LastNonPositionalHandler = drawable;
                return true;
            }
        }

        LastNonPositionalHandler = null;
        return false;
    }

    public bool DispatchKeyDown(KeyEvent e) => dispatchNonPositional(d => d.TriggerKeyDown(e));

    public bool DispatchKeyUp(KeyEvent e) => dispatchNonPositional(d => d.TriggerKeyUp(e));

    public bool DispatchTextInput(TextInputEvent e) => dispatchNonPositional(d => d.TriggerTextInput(e));

    public bool DispatchTextEditing(TextEditingEvent e) => dispatchNonPositional(d => d.TriggerTextEditing(e));

    public bool DispatchGamepadButtonDown(GamepadButtonEvent e) => dispatchNonPositional(d => d.TriggerGamepadButtonDown(e));

    public bool DispatchGamepadButtonUp(GamepadButtonEvent e) => dispatchNonPositional(d => d.TriggerGamepadButtonUp(e));

    public bool DispatchGamepadAxisMotion(GamepadAxisEvent e) => dispatchNonPositional(d => d.TriggerGamepadAxisMotion(e));

    /// <summary>
    /// Delivers a gamepad connected event to every entry in the non-positional queue (broadcast; no
    /// consumption), mirroring the recursive <c>OnGamepadConnected</c> fan-out.
    /// </summary>
    public void DispatchGamepadConnected(GamepadConnectedEvent e)
    {
        for (int i = 0; i < nonPositionalQueue.Count; i++)
        {
            var drawable = nonPositionalQueue[i];
            if (drawable.IsLoaded)
                drawable.TriggerGamepadConnected(e);
        }
    }

    /// <summary>
    /// Delivers a gamepad disconnected event to every entry in the non-positional queue (broadcast).
    /// </summary>
    public void DispatchGamepadDisconnected(GamepadDisconnectedEvent e)
    {
        for (int i = 0; i < nonPositionalQueue.Count; i++)
        {
            var drawable = nonPositionalQueue[i];
            if (drawable.IsLoaded)
                drawable.TriggerGamepadDisconnected(e);
        }
    }

    #endregion

    #region Raw event observation (state-only; does not dispatch)

    public void HandleMouseMove(Vector2 position) => CurrentState.SetMousePosition(position);

    public void HandleMouseDown(MouseButton button, Vector2 position)
    {
        CurrentState.SetMousePosition(position);
        CurrentState.SetMouseButton(button, true);
    }

    public void HandleMouseUp(MouseButton button, Vector2 position)
    {
        CurrentState.SetMousePosition(position);
        CurrentState.SetMouseButton(button, false);
    }

    public void HandleScroll(Vector2 position) => CurrentState.SetMousePosition(position);

    public void HandleKeyDown(Key key) => CurrentState.SetKey(key, true);

    public void HandleKeyUp(Key key) => CurrentState.SetKey(key, false);

    public void HandleGamepadButtonDown(int deviceId, GamepadButton button) => CurrentState.SetGamepadButton(deviceId, button, true);

    public void HandleGamepadButtonUp(int deviceId, GamepadButton button) => CurrentState.SetGamepadButton(deviceId, button, false);

    public void HandleGamepadAxis(int deviceId, GamepadAxis axis, float value) => CurrentState.SetGamepadAxis(deviceId, axis, value);

    public void HandleGamepadConnected(int deviceId) => CurrentState.AddGamepad(deviceId);

    public void HandleGamepadDisconnected(int deviceId) => CurrentState.RemoveGamepad(deviceId);

    #endregion
}
