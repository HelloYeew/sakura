// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Input.Bindings;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Input;

/// <summary>
/// Owns a single authoritative <see cref="InputState"/>, builds two explicit, inspectable input
/// queues each frame, dispatches events down them, and owns focus (<see cref="IFocusManager"/>).
/// </summary>
public class InputManager : IFocusManager
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
    private Drawable lastQueueRoot;

    public void BuildQueues(Drawable root, Vector2 positionalPoint)
    {
        nonPositionalQueue.Clear();
        positionalQueue.Clear();

        lastQueueRoot = root;

        if (root == null)
            return;

        buildNonPositional(root, isRoot: true);
        buildPositional(root, positionalPoint, isRoot: true);
    }

    /// <summary>
    /// Rebuilds the queues using the current mouse position from <see cref="CurrentState"/>.
    /// </summary>
    public void BuildQueues(Drawable root) => BuildQueues(root, CurrentState.MousePosition);

    private void buildNonPositional(Drawable drawable, bool isRoot = false)
    {
        // The explicitly-chosen root is always walked; only descendants are filtered by the opt-in.
        // This lets a subtree root (e.g. a ManualInputManager that has opted itself out of its
        // parent's queues) still build its own queues over itself.
        if (!drawable.IsLoaded || (!isRoot && !drawable.HandleNonPositionalInput))
            return;

        if (drawable is Container container)
        {
            var children = container.Children;
            for (int i = 0; i < children.Count; i++)
                buildNonPositional(children[i]);
        }

        nonPositionalQueue.Add(drawable);
    }

    private void buildPositional(Drawable drawable, Vector2 point, bool isRoot = false)
    {
        if (!drawable.IsLoaded || !drawable.IsAlive || drawable.IsHidden || (!isRoot && !drawable.HandlePositionalInput))
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
    /// Collects descendants of <paramref name="root"/> implementing <see cref="IKeyBindingHandler{T}"/>
    /// in front-to-back order (front-most child first), so the visually top-most handler gets first
    /// refusal. Centralises the non-positional handler traversal that <c>KeyBindingContainer</c> used
    /// to walk itself. Loaded + alive + non-positional-opted-in drawables only; the root itself is not
    /// included (a container dispatches to its descendants).
    /// </summary>
    public List<IKeyBindingHandler<T>> CollectKeyBindingHandlers<T>(Drawable root) where T : struct
    {
        var results = new List<IKeyBindingHandler<T>>();

        if (root is Container container)
            collectKeyBindingHandlers(container, results);

        return results;
    }

    private static void collectKeyBindingHandlers<T>(Container container, List<IKeyBindingHandler<T>> results) where T : struct
    {
        var sorted = container.SortedChildren;

        for (int i = sorted.Count - 1; i >= 0; i--)
        {
            var child = sorted[i];

            if (!child.IsLoaded || !child.IsAlive || !child.HandleNonPositionalInput)
                continue;

            if (child is IKeyBindingHandler<T> handler)
                results.Add(handler);

            if (child is Container childContainer)
                collectKeyBindingHandlers(childContainer, results);
        }
    }

    /// <summary>
    /// The queue entry that consumed the most recent non-positional event (keyboard / text /
    /// gamepad), or <c>null</c> if the last event was unhandled. Exposed for the debug overlay so it
    /// can highlight "this is where the key went".
    /// </summary>
    public Drawable? LastNonPositionalHandler { get; private set; }

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

    #region Positional dispatch

    /// <summary>
    /// The drawable that consumed the most recent positional event (mouse / scroll), or
    /// <c>null</c> if unhandled. Exposed for the debug overlay.
    /// </summary>
    public Drawable? LastPositionalHandler { get; private set; }

    private Drawable? dragCaptureTarget;

    /// <summary>
    /// The drawable currently capturing the drag (set on the mouse-down that started a left-button
    /// drag, cleared on mouse-up). While set, mouse-move and mouse-up route only to it, so a drag
    /// stays with its target even when the cursor leaves its bounds. Replaces the per-container
    /// <c>draggedChild</c> bookkeeping. Exposed for the overlay.
    /// </summary>
    public Drawable? DragCaptureTarget => dragCaptureTarget;

    private readonly List<Drawable> hoveredDrawables = new List<Drawable>();

    private readonly HashSet<Drawable> hoverBlockers = new HashSet<Drawable>();

    public IReadOnlyList<Drawable> HoveredDrawables => hoveredDrawables;

    /// <summary>
    /// Dispatches a mouse-down down the positional queue (front-to-back). The first entry that
    /// handles it becomes the drag-capture target (so subsequent moves/up stay with it).
    /// </summary>
    public bool DispatchMouseDown(MouseButtonEvent e)
    {
        for (int i = 0; i < positionalQueue.Count; i++)
        {
            var drawable = positionalQueue[i];

            if (!drawable.IsLoaded || !drawable.IsAlive || drawable.IsHidden)
                continue;

            if (drawable.TriggerMouseDown(e))
            {
                if (e.Button == MouseButton.Left)
                    dragCaptureTarget = drawable;

                LastPositionalHandler = drawable;
                return true;
            }
        }

        LastPositionalHandler = null;
        return false;
    }

    /// <summary>
    /// Dispatches a mouse-up. If a drag is in progress, only the captured target receives it (and the
    /// capture is released); otherwise it walks the positional queue front-to-back.
    /// </summary>
    public bool DispatchMouseUp(MouseButtonEvent e)
    {
        if (dragCaptureTarget != null)
        {
            var target = dragCaptureTarget;
            dragCaptureTarget = null;

            bool capturedHandled = target.IsLoaded && target.TriggerMouseUp(e);
            LastPositionalHandler = capturedHandled ? target : null;
            return capturedHandled;
        }

        for (int i = 0; i < positionalQueue.Count; i++)
        {
            var drawable = positionalQueue[i];

            if (!drawable.IsLoaded || !drawable.IsAlive || drawable.IsHidden)
                continue;

            if (drawable.TriggerMouseUp(e))
            {
                LastPositionalHandler = drawable;
                return true;
            }
        }

        LastPositionalHandler = null;
        return false;
    }

    /// <summary>
    /// Handles a mouse-move: routes the move to the drag-capture target (if any) for drag delivery,
    /// then reconciles hover enter/leave against the freshly built positional queue.
    /// </summary>
    public bool DispatchMouseMove(MouseEvent e)
    {
        bool handled = false;

        // A drag in progress stays with its captured target, even outside its bounds. The target's
        // IsDragged flag short-circuits its OnMouseMove straight to OnDrag; recursion is suppressed
        // via TriggerMouseMove so a captured container does not re-route into its children.
        if (dragCaptureTarget != null && dragCaptureTarget.IsLoaded)
            handled = dragCaptureTarget.TriggerMouseMove(e);

        updateHover(e);
        return handled;
    }

    /// <summary>
    /// Re-evaluates hover at the current mouse position without an actual mouse move. Use when the
    /// scene changes under a stationary cursor (e.g. content scrolls), so a drawable that moved under
    /// or out from under the cursor gets the correct <c>OnHover</c>/<c>OnHoverLost</c>. Rebuilds the
    /// positional queue against the last root passed to <see cref="BuildQueues"/>.
    /// </summary>
    public void RefreshHover()
    {
        if (lastQueueRoot == null)
            return;

        BuildQueues(lastQueueRoot, CurrentState.MousePosition);
        updateHover(new MouseEvent(new MouseState { Position = CurrentState.MousePosition }));
    }

    /// <summary>
    /// Dispatches a scroll down the positional queue (front-to-back); the first entry that handles
    /// it wins.
    /// </summary>
    public bool DispatchScroll(ScrollEvent e)
    {
        for (int i = 0; i < positionalQueue.Count; i++)
        {
            var drawable = positionalQueue[i];

            if (!drawable.IsLoaded || !drawable.IsAlive || drawable.IsHidden)
                continue;

            if (drawable.TriggerScroll(e))
            {
                LastPositionalHandler = drawable;
                return true;
            }
        }

        LastPositionalHandler = null;
        return false;
    }

    /// <summary>
    /// Reconciles hover state against the current positional queue: drawables that are in the queue
    /// (i.e. under the cursor and opted in) but were not hovered get <see cref="Drawable.IsHovered"/>
    /// set and <c>OnHover</c>; drawables that were hovered but are no longer in the queue get
    /// <c>OnHoverLost</c>. Mirrors the per-drawable hover toggling the recursive path performed.
    /// </summary>
    private void updateHover(MouseEvent e)
    {
        // Walk the positional queue front-to-back, marking entries as hovered until one "blocks" by
        // returning true from OnHover (e.g. a visible OverlayContainer). Everything in front of and
        // including the blocker is hovered; everything behind it is treated as not hovered. This
        // mirrors the recursive path, where a child returning true from OnMouseMove broke the loop.
        var newlyHovered = new HashSet<Drawable>();
        bool blocked = false;

        for (int i = 0; i < positionalQueue.Count && !blocked; i++)
        {
            var drawable = positionalQueue[i];
            newlyHovered.Add(drawable);

            if (!drawable.IsHovered)
            {
                drawable.IsHovered = true;
                if (drawable.OnHover(e))
                    blocked = true;
            }
            else if (hoverBlockers.Contains(drawable))
            {
                // Already hovered and known to block: stop here so drawables behind it stay unhovered.
                blocked = true;
            }

            if (blocked)
                hoverBlockers.Add(drawable);
        }

        // Leave: previously hovered but no longer in the (front-of-blocker) hovered set, or gone.
        for (int i = hoveredDrawables.Count - 1; i >= 0; i--)
        {
            var drawable = hoveredDrawables[i];

            if (!drawable.IsLoaded || !drawable.IsAlive || !newlyHovered.Contains(drawable))
            {
                drawable.IsHovered = false;
                hoverBlockers.Remove(drawable);
                if (drawable.IsLoaded)
                    drawable.OnHoverLost(e);
                hoveredDrawables.RemoveAt(i);
            }
        }

        // Record the freshly entered drawables (front-of-blocker only).
        foreach (var drawable in newlyHovered)
        {
            if (!hoveredDrawables.Contains(drawable))
                hoveredDrawables.Add(drawable);
        }
    }

    /// <summary>
    /// Clears all transient input state: pressed keys/buttons/gamepads, drag capture, hover set, and
    /// the queues. Mainly for tests, so input does not leak from one test into the next when a
    /// manager instance is shared across a fixture. The mouse position is preserved.
    /// </summary>
    public void Reset()
    {
        CurrentState.Clear();
        dragCaptureTarget = null;

        foreach (var drawable in hoveredDrawables)
            drawable.IsHovered = false;
        hoveredDrawables.Clear();
        hoverBlockers.Clear();

        nonPositionalQueue.Clear();
        positionalQueue.Clear();
        LastNonPositionalHandler = null;
        LastPositionalHandler = null;

        if (focusedDrawable != null)
            focusedDrawable.HasFocus = false;
        focusedDrawable = null;
        focusClaimedByClick = false;
        focusStack.Clear();
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

    #region Focus management (IFocusManager)

    private readonly List<Drawable> focusStack = new List<Drawable>();

    private Drawable? focusedDrawable;

    private bool focusClaimedByClick;

    /// <summary>
    /// The drawable that currently holds focus, or null if nothing is focused.
    /// </summary>
    public Drawable? FocusedDrawable => focusedDrawable;

    /// <summary>
    /// The current focus stack (back-most first), exposed for inspection / the debug overlay.
    /// </summary>
    public IReadOnlyList<Drawable> FocusStack => focusStack;

    /// <summary>
    /// True if a focusable drawable claimed focus during the most recent mouse-down dispatch.
    /// </summary>
    public bool WasFocusClaimedByLastClick => focusClaimedByClick;

    /// <summary>
    /// Resets the "focus was claimed by this click" tracker. Called before a mouse-down dispatch.
    /// </summary>
    public void BeginMouseDownFocusTracking() => focusClaimedByClick = false;

    /// <summary>
    /// Changes the currently focused drawable, maintaining a stack so focus is restored to the
    /// previous holder when the current one is released or removed.
    /// </summary>
    public bool ChangeFocus(Drawable? potentialFocusTarget)
    {
        var focusedBefore = focusedDrawable;

        if (focusedDrawable == potentialFocusTarget)
        {
            if (potentialFocusTarget != null)
                focusClaimedByClick = true;
            return true;
        }

        if (potentialFocusTarget != null && !potentialFocusTarget.AcceptsFocus)
            return false;

        if (potentialFocusTarget == null)
        {
            if (focusedDrawable != null)
            {
                focusedDrawable.HasFocus = false;
                focusedDrawable.OnFocusLost(new FocusLostEvent());
                focusStack.Remove(focusedDrawable);
            }

            focusedDrawable = null;

            while (focusStack.Count > 0)
            {
                var previous = focusStack[^1];

                if (previous.IsAlive && previous.IsLoaded && previous.AcceptsFocus)
                {
                    focusedDrawable = previous;
                    focusStack.RemoveAt(focusStack.Count - 1);

                    focusedDrawable.HasFocus = true;
                    focusedDrawable.OnFocus(new FocusEvent());
                    break;
                }

                focusStack.RemoveAt(focusStack.Count - 1);
            }

            Logger.Verbose($"🐭 Focus changed from {focusedBefore?.ToString() ?? "null"} to {focusedDrawable?.ToString() ?? "null"}");
            return true;
        }

        if (focusedDrawable != null)
        {
            focusedDrawable.HasFocus = false;
            focusedDrawable.OnFocusLost(new FocusLostEvent());

            if (!focusStack.Contains(focusedDrawable))
            {
                focusStack.Add(focusedDrawable);
            }
        }

        focusedDrawable = potentialFocusTarget;
        focusStack.Remove(focusedDrawable);

        focusedDrawable.HasFocus = true;
        focusedDrawable.OnFocus(new FocusEvent());
        focusClaimedByClick = true;

        Logger.Verbose($"🐭 Focus changed from {focusedBefore?.ToString() ?? "null"} to {focusedDrawable?.ToString() ?? "null"}");
        return true;
    }

    /// <summary>
    /// Evaluates focus state when a drawable requests focus (e.g. a freshly shown overlay).
    /// </summary>
    public void TriggerFocusContention(Drawable? triggerSource)
    {
        if (triggerSource != null && triggerSource.RequestsFocus)
            ChangeFocus(triggerSource);
    }

    #endregion
}
