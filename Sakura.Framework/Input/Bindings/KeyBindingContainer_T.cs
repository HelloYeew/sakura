// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Input.Bindings;

/// <summary>
/// A <see cref="Container"/> that maps key combinations to strongly-typed actions and dispatches
/// press/release events to descendant <see cref="IKeyBindingHandler{T}"/> implementers.
/// </summary>
/// <typeparam name="T">The action enum type. Must be a value type (typically an <c>enum</c>).</typeparam>
public abstract partial class KeyBindingContainer<T> : KeyBindingContainer where T : struct
{
    /// <summary>
    /// The bindings supplied by default. Override to provide your application's key map.
    /// </summary>
    public virtual IEnumerable<KeyBinding> DefaultKeyBindings => Enumerable.Empty<KeyBinding>();

    /// <summary>
    /// The live set of bindings used for matching. Initialised from <see cref="DefaultKeyBindings"/>
    /// and may be reassigned at runtime to support rebinding.
    /// </summary>
    public List<KeyBinding> KeyBindings { get; set; } = new List<KeyBinding>();

    /// <summary>
    /// Bindings currently considered "pressed", each paired with the handler (if any) that claimed it.
    /// </summary>
    private readonly List<PressedBinding> pressedBindings = new List<PressedBinding>();

    private readonly struct PressedBinding
    {
        public readonly KeyBinding Binding;
        public readonly IKeyBindingHandler<T>? Handler;

        public PressedBinding(KeyBinding binding, IKeyBindingHandler<T>? handler)
        {
            Binding = binding;
            Handler = handler;
        }
    }

    /// <summary>
    /// An optional external source of bindings (e.g. a <see cref="KeyBindingStore{T}"/>) consulted by
    /// <see cref="ReloadMappings"/>. When null, <see cref="DefaultKeyBindings"/> is used. To pick up
    /// runtime changes, subscribe the store's change notification to <see cref="ReloadMappings"/>.
    /// </summary>
    public IKeyBindingSource? Source { get; set; }

    public override void LoadComplete()
    {
        base.LoadComplete();

        if (KeyBindings.Count == 0)
            ReloadMappings();
    }

    /// <summary>
    /// Recomputes the live <see cref="KeyBindings"/> from <see cref="Source"/> (or
    /// <see cref="DefaultKeyBindings"/> if no source is set) and safely clears any in-flight pressed
    /// state, releasing currently-held actions first so no action is left stranded after a rebind.
    /// </summary>
    public void ReloadMappings()
    {
        // Release anything currently held so handlers see a clean release before the map changes.
        releaseAllPressedBindings();

        KeyBindings = Source != null
            ? Source.GetBindings(GetType()).ToList()
            : DefaultKeyBindings.ToList();
    }

    /// <summary>
    /// The currently-held inputs in <see cref="InputKey"/> space, read from the shared
    /// <see cref="InputState"/> of the containing <see cref="InputManager"/> (keyboard keys folded to
    /// logical modifiers, plus mouse and gamepad buttons), unioned with the logical modifiers carried
    /// by the current event's <see cref="KeyModifiers"/> flags (so combinations match even when the
    /// platform reports a modifier as a flag rather than a discrete key). An optional
    /// <paramref name="transient"/> key (e.g. a momentary scroll key) is appended.
    /// </summary>
    private List<InputKey> currentPressedKeys(KeyModifiers eventModifiers = KeyModifiers.None, InputKey transient = InputKey.None)
    {
        var result = new List<InputKey>();

        var manager = GetContainingInputManager();
        if (manager != null)
            result.AddRange(manager.CurrentState.GetPressedInputKeys());

        foreach (var modifier in modifierKeys(eventModifiers))
        {
            if (!result.Contains(modifier))
                result.Add(modifier);
        }

        if (transient != InputKey.None && !result.Contains(transient))
            result.Add(transient);

        return result;
    }

    public override bool OnKeyDown(KeyEvent e)
    {
        // Let descendants (e.g. focused text boxes) consume the raw event first.
        if (base.OnKeyDown(e))
            return true;

        // The pressed state already lives in the shared InputState; just recompute matches. Repeats
        // are driven off the event's IsRepeat flag (and only forwarded when SendRepeats is set).
        if (e.IsRepeat)
            return SendRepeats && updatePressedBindings(currentPressedKeys(e.Modifiers), repeat: true, scrollDelta: Vector2.Zero);

        return updatePressedBindings(currentPressedKeys(e.Modifiers), repeat: false, scrollDelta: Vector2.Zero);
    }

    public override bool OnKeyUp(KeyEvent e)
    {
        base.OnKeyUp(e);

        releaseBindingsNoLongerMatched(currentPressedKeys(e.Modifiers));
        return false;
    }

    public override bool OnMouseDown(MouseButtonEvent e)
    {
        if (base.OnMouseDown(e))
            return true;

        return updatePressedBindings(currentPressedKeys(), repeat: false, scrollDelta: Vector2.Zero);
    }

    public override bool OnMouseUp(MouseButtonEvent e)
    {
        base.OnMouseUp(e);

        releaseBindingsNoLongerMatched(currentPressedKeys());
        return false;
    }

    public override bool OnScroll(ScrollEvent e)
    {
        if (base.OnScroll(e))
            return true;

        var (vertical, horizontal) = InputKeyExtensions.FromScrollDelta(e.ScrollDelta);

        bool handled = false;
        handled |= handleScroll(vertical, e.ScrollDelta);
        handled |= handleScroll(horizontal, e.ScrollDelta);
        return handled;
    }

    private bool handleScroll(InputKey scrollKey, Vector2 delta)
    {
        if (scrollKey == InputKey.None)
            return false;

        var pressed = currentPressedKeys(transient: scrollKey);
        bool handled = updatePressedBindings(pressed, repeat: false, scrollDelta: delta);
        releaseBindingsNoLongerMatched(currentPressedKeys());

        return handled;
    }

    public override bool OnGamepadButtonDown(GamepadButtonEvent e)
    {
        if (base.OnGamepadButtonDown(e))
            return true;

        return updatePressedBindings(currentPressedKeys(), repeat: false, scrollDelta: Vector2.Zero);
    }

    public override bool OnGamepadButtonUp(GamepadButtonEvent e)
    {
        base.OnGamepadButtonUp(e);

        releaseBindingsNoLongerMatched(currentPressedKeys());
        return false;
    }

    /// <summary>
    /// Recomputes which bindings are satisfied by <paramref name="pressedKeys"/> and fires press
    /// events for any newly-satisfied bindings, subject to the simultaneous-binding mode.
    /// </summary>
    private bool updatePressedBindings(IReadOnlyCollection<InputKey> pressedKeys, bool repeat, Vector2 scrollDelta)
    {
        var newlyPressed = KeyBindings
                           .Where(b => b.KeyCombination.IsPressed(pressedKeys, MatchingMode))
                           .Where(b => repeat || !isBindingActive(b))
                           .ToList();

        if (newlyPressed.Count == 0)
            return false;

        // In None mode, only the most specific (largest) combination should win, and any existing
        // active binding is released first.
        if (SimultaneousMode == SimultaneousBindingMode.None)
        {
            releaseAllPressedBindings();
            newlyPressed = newlyPressed
                           .OrderByDescending(b => b.KeyCombination.Keys.Length)
                           .Take(1)
                           .ToList();
        }

        bool handled = false;

        foreach (var binding in newlyPressed)
        {
            // In Unique mode, do not press the same action twice concurrently.
            if (SimultaneousMode == SimultaneousBindingMode.Unique && repeat == false
                && pressedBindings.Any(p => p.Binding.Action.Equals(binding.Action)))
            {
                continue;
            }

            var (claimed, handler) = dispatchPress(binding.GetAction<T>(), repeat, scrollDelta);
            handled |= claimed;

            if (!repeat)
                pressedBindings.Add(new PressedBinding(binding, handler));
        }

        return handled;
    }

    private bool isBindingActive(KeyBinding binding)
        => pressedBindings.Any(p => ReferenceEquals(p.Binding, binding));

    /// <summary>
    /// Releases any active binding whose combination is no longer satisfied by
    /// <paramref name="pressedKeys"/>.
    /// </summary>
    private void releaseBindingsNoLongerMatched(IReadOnlyCollection<InputKey> pressedKeys)
    {
        for (int i = pressedBindings.Count - 1; i >= 0; i--)
        {
            var pressed = pressedBindings[i];

            if (!pressed.Binding.KeyCombination.IsPressed(pressedKeys, MatchingMode))
            {
                releasePressedBinding(i);
            }
        }
    }

    private void releaseAllPressedBindings()
    {
        for (int i = pressedBindings.Count - 1; i >= 0; i--)
            releasePressedBinding(i);
    }

    private void releasePressedBinding(int index)
    {
        var pressed = pressedBindings[index];
        pressedBindings.RemoveAt(index);

        // In Unique mode the action stays pressed while any other active binding maps to it.
        if (SimultaneousMode == SimultaneousBindingMode.Unique
            && pressedBindings.Any(p => p.Binding.Action.Equals(pressed.Binding.Action)))
        {
            return;
        }

        pressed.Handler?.OnReleased(new KeyBindingReleaseEvent<T>(pressed.Binding.GetAction<T>()));
    }

    private static IEnumerable<InputKey> modifierKeys(KeyModifiers modifiers)
    {
        if ((modifiers & KeyModifiers.Shift) != 0)
            yield return InputKey.Shift;
        if ((modifiers & KeyModifiers.Control) != 0)
            yield return InputKey.Control;
        if ((modifiers & KeyModifiers.Alt) != 0)
            yield return InputKey.Alt;
    }
    private (bool handled, IKeyBindingHandler<T>? handler) dispatchPress(T action, bool repeat, Vector2 scrollDelta)
    {
        var e = new KeyBindingPressEvent<T>(action, repeat, scrollDelta);

        foreach (var handler in getHandlers())
        {
            if (handler.OnPressed(e))
                return (true, handler);
        }

        return (false, null);
    }

    /// <summary>
    /// Collects descendant <see cref="IKeyBindingHandler{T}"/> implementers in front-to-back order.
    /// The traversal now lives on the <see cref="InputManager"/> (single source of truth); when no
    /// manager is reachable (e.g. a detached container), this container has no handlers to dispatch to.
    /// </summary>
    private IEnumerable<IKeyBindingHandler<T>> getHandlers()
        => GetContainingInputManager()?.CollectKeyBindingHandlers<T>(this) ?? new List<IKeyBindingHandler<T>>();
}
