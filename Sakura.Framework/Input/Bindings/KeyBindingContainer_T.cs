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
    /// The keys currently held down, in press order. Physical modifiers are folded to logical form.
    /// </summary>
    private readonly List<InputKey> pressedKeys = new List<InputKey>();

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

    public override void LoadComplete()
    {
        base.LoadComplete();

        if (KeyBindings.Count == 0)
            KeyBindings = DefaultKeyBindings.ToList();
    }

    public override bool OnKeyDown(KeyEvent e)
    {
        // Let descendants (e.g. focused text boxes) consume the raw event first.
        if (base.OnKeyDown(e))
            return true;

        bool handled = false;

        // Expand modifier flags into logical modifier InputKeys so combinations like Ctrl+A work even
        // when the platform reports the modifier via KeyModifiers rather than a discrete key event.
        foreach (var modifier in modifierKeys(e.Modifiers))
            handled |= addPressedKey(modifier, e.IsRepeat);

        handled |= addPressedKey(InputKeyExtensions.FromKey(e.Key), e.IsRepeat);
        return handled;
    }

    public override bool OnKeyUp(KeyEvent e)
    {
        base.OnKeyUp(e);

        var released = InputKeyExtensions.FromKey(e.Key);
        bool handled = removePressedKey(released);

        // If the released key is itself a (physical) modifier, clear its logical form directly —
        // we store modifiers in logical form, and the platform's reported modifier flags may still
        // briefly include the key being released.
        var releasedLogical = released.ToLogicalModifier();
        if (releasedLogical != InputKey.None)
            handled |= removePressedKey(releasedLogical);

        // Also reconcile against the modifier flags: release any logical modifier no longer reported.
        var activeModifiers = modifierKeys(e.Modifiers).ToHashSet();
        foreach (var modifier in new[] { InputKey.Shift, InputKey.Control, InputKey.Alt, InputKey.Super })
        {
            if (!activeModifiers.Contains(modifier))
                handled |= removePressedKey(modifier);
        }

        return handled;
    }

    public override bool OnMouseDown(MouseButtonEvent e)
    {
        if (base.OnMouseDown(e))
            return true;

        return addPressedKey(InputKeyExtensions.FromMouseButton(e.Button), repeat: false);
    }

    public override bool OnMouseUp(MouseButtonEvent e)
    {
        base.OnMouseUp(e);
        return removePressedKey(InputKeyExtensions.FromMouseButton(e.Button));
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

        // Press, dispatch (with the scroll delta attached), then release immediately.
        pressedKeys.Add(scrollKey);
        bool handled = updatePressedBindings(repeat: false, scrollDelta: delta);
        pressedKeys.Remove(scrollKey);
        releaseBindingsNoLongerMatched();

        return handled;
    }

    public override bool OnGamepadButtonDown(GamepadButtonEvent e)
    {
        if (base.OnGamepadButtonDown(e))
            return true;

        return addPressedKey(InputKeyExtensions.FromGamepadButton(e.Button), repeat: false);
    }

    public override bool OnGamepadButtonUp(GamepadButtonEvent e)
    {
        base.OnGamepadButtonUp(e);
        return removePressedKey(InputKeyExtensions.FromGamepadButton(e.Button));
    }

    private bool addPressedKey(InputKey key, bool repeat)
    {
        if (key == InputKey.None)
            return false;

        if (pressedKeys.Contains(key))
        {
            if (repeat && SendRepeats)
                return updatePressedBindings(repeat: true, scrollDelta: Vector2.Zero);

            return false;
        }

        pressedKeys.Add(key);
        return updatePressedBindings(repeat: false, scrollDelta: Vector2.Zero);
    }

    private bool removePressedKey(InputKey key)
    {
        if (key == InputKey.None)
            return false;

        if (!pressedKeys.Remove(key))
            return false;

        releaseBindingsNoLongerMatched();
        return true;
    }

    /// <summary>
    /// Recomputes which bindings are satisfied by the current <see cref="pressedKeys"/> and fires
    /// press events for any newly-satisfied bindings, subject to the simultaneous-binding mode.
    /// </summary>
    private bool updatePressedBindings(bool repeat, Vector2 scrollDelta)
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
    /// Releases any active binding whose combination is no longer satisfied.
    /// </summary>
    private void releaseBindingsNoLongerMatched()
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
    /// Collects descendant <see cref="IKeyBindingHandler{T}"/> implementers in front-to-back order
    /// (front-most child first), so the visually top-most handler gets first refusal — mirroring how
    /// positional input is resolved elsewhere in the framework.
    /// </summary>
    private IEnumerable<IKeyBindingHandler<T>> getHandlers()
    {
        var results = new List<IKeyBindingHandler<T>>();
        collect(this, results);
        return results;
    }

    private static void collect(Container container, List<IKeyBindingHandler<T>> results)
    {
        var sorted = container.SortedChildren;

        // Iterate front-to-back (highest depth / latest insertion first).
        for (int i = sorted.Count - 1; i >= 0; i--)
        {
            var child = sorted[i];

            if (!child.IsLoaded || !child.IsAlive)
                continue;

            if (child is IKeyBindingHandler<T> handler)
                results.Add(handler);

            if (child is Container childContainer)
                collect(childContainer, results);
        }
    }
}
