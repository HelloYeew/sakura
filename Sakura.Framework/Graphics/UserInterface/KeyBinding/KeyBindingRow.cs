// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Input.Bindings;

namespace Sakura.Framework.Graphics.UserInterface.KeyBinding;

public abstract partial class KeyBindingRow<T> : Container where T : struct, Enum
{
    /// <summary>
    /// The action this row rebinds.
    /// </summary>
    public T Action { get; }

    /// <summary>
    /// The current combination in each slot. <see cref="KeyCombination.NONE"/> means unbound.
    /// </summary>
    private readonly List<KeyCombination> slots;

    /// <summary>
    /// Number of binding slots in this row.
    /// </summary>
    public int SlotCount => slots.Count;

    /// <summary>
    /// The slot currently being captured, or -1 when not capturing.
    /// </summary>
    private int capturingSlot = -1;

    /// <summary>
    /// The keys held down so far during the current capture, in press order.
    /// </summary>
    private readonly List<InputKey> capturedKeys = new List<InputKey>();

    /// <summary>
    /// Fired when the combinations for this action change (rebind or clear). The full slot list is
    /// supplied so the panel can persist all of them at once.
    /// </summary>
    public event Action<T, IReadOnlyList<KeyCombination>>? Changed;

    /// <summary>
    /// Text shown in a slot while waiting for the user to press a combination during capture.
    /// </summary>
    protected virtual string CapturePrompt => "Press keys…";

    /// <summary>
    /// Text shown for an empty (unbound) slot.
    /// </summary>
    protected virtual string UnboundText => "(unbound)";

    public override bool AcceptsFocus => true;

    protected KeyBindingRow(T action, IEnumerable<KeyCombination> current, int slotCount)
    {
        if (slotCount < 1)
            throw new ArgumentOutOfRangeException(nameof(slotCount), "A row must have at least one slot.");

        Action = action;

        slots = current.Take(slotCount).ToList();
        while (slots.Count < slotCount)
            slots.Add(KeyCombination.NONE);

        RelativeSizeAxes = Axes.X;
        Width = 1;
        Height = 32;
    }

    public override void LoadComplete()
    {
        base.LoadComplete();

        UpdateActionText(Action.ToString());
        for (int i = 0; i < slots.Count; i++)
            UpdateSlotText(i, displayText(slots[i]));
    }

    private string displayText(KeyCombination combo) => combo.Keys.Length == 0 ? UnboundText : combo.DisplayString;

    /// <summary>
    /// The combination in a given slot.
    /// </summary>
    public KeyCombination GetSlot(int index) => slots[index];

    /// <summary>
    /// Begins capturing a new combination for the given slot. Called by subclasses when a slot is
    /// activated (e.g. clicked).
    /// </summary>
    protected void BeginCapture(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= slots.Count)
            return;

        // Switching capture target mid-capture: drop the in-progress one.
        capturedKeys.Clear();
        capturingSlot = slotIndex;

        // Ensure the row holds focus so subsequent key events arrive here. Slot buttons are
        // non-focusable, so without this an outer focus manager would clear focus on the click.
        GetContainingFocusManager()?.ChangeFocus(this);

        UpdateSlotText(slotIndex, CapturePrompt);
        OnCaptureStarted(slotIndex);
    }

    /// <summary>
    /// Clears the given slot to unbound and notifies listeners. Called by subclasses (e.g. a clear
    /// button).
    /// </summary>
    protected void ClearSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= slots.Count)
            return;

        cancelCaptureInternal(notify: false);

        slots[slotIndex] = KeyCombination.NONE;
        UpdateSlotText(slotIndex, displayText(KeyCombination.NONE));
        Changed?.Invoke(Action, slots.AsReadOnly());
    }

    private void endCapture(KeyCombination result)
    {
        int slot = capturingSlot;
        capturingSlot = -1;
        OnCaptureEnded(slot);

        if (result.Keys.Length > 0)
        {
            slots[slot] = result;
            UpdateSlotText(slot, displayText(result));
            Changed?.Invoke(Action, slots.AsReadOnly());
        }
        else
        {
            UpdateSlotText(slot, displayText(slots[slot]));
        }
    }

    public override bool OnKeyDown(KeyEvent e)
    {
        if (capturingSlot < 0)
            return base.OnKeyDown(e);

        // Escape is bindable like any other key — no special cancel handling here.
        foreach (var modifier in modifierKeys(e.Modifiers))
            addCaptured(modifier);

        addCaptured(InputKeyExtensions.FromKey(e.Key));
        return true;
    }

    public override bool OnKeyUp(KeyEvent e)
    {
        if (capturingSlot < 0)
            return base.OnKeyUp(e);

        finalise();
        return true;
    }

    public override bool OnMouseDown(MouseButtonEvent e)
    {
        if (capturingSlot < 0)
            return base.OnMouseDown(e);

        addCaptured(InputKeyExtensions.FromMouseButton(e.Button));
        return true;
    }

    public override bool OnMouseUp(MouseButtonEvent e)
    {
        if (capturingSlot < 0)
            return base.OnMouseUp(e);

        finalise();
        return true;
    }

    public override bool OnGamepadButtonDown(GamepadButtonEvent e)
    {
        if (capturingSlot < 0)
            return base.OnGamepadButtonDown(e);

        addCaptured(InputKeyExtensions.FromGamepadButton(e.Button));
        return true;
    }

    public override bool OnGamepadButtonUp(GamepadButtonEvent e)
    {
        if (capturingSlot < 0)
            return base.OnGamepadButtonUp(e);

        finalise();
        return true;
    }

    public override bool OnScroll(ScrollEvent e)
    {
        if (capturingSlot < 0)
            return base.OnScroll(e);

        // A scroll notch has no release. Capture the scroll direction(s) and finalise immediately,
        // combined with any modifiers already held during capture.
        var (vertical, horizontal) = InputKeyExtensions.FromScrollDelta(e.ScrollDelta);
        addCaptured(vertical);
        addCaptured(horizontal);
        finalise();
        return true;
    }

    public override void OnFocusLost(FocusLostEvent e)
    {
        // Clicking elsewhere cancels an in-progress capture without changing the binding.
        cancelCaptureInternal(notify: false);
        base.OnFocusLost(e);
    }

    private void cancelCaptureInternal(bool notify)
    {
        if (capturingSlot < 0)
            return;

        int slot = capturingSlot;
        capturingSlot = -1;
        capturedKeys.Clear();
        OnCaptureEnded(slot);
        UpdateSlotText(slot, displayText(slots[slot]));
    }

    private void addCaptured(InputKey key)
    {
        if (key == InputKey.None || capturedKeys.Contains(key))
            return;

        capturedKeys.Add(key);
        UpdateSlotText(capturingSlot, new KeyCombination(capturedKeys).DisplayString);
    }

    private void finalise()
    {
        if (capturedKeys.Count == 0)
            return;

        var result = new KeyCombination(capturedKeys);
        capturedKeys.Clear();
        endCapture(result);
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

    /// <summary>
    /// Sets the displayed action name. Called once on load.
    /// </summary>
    protected abstract void UpdateActionText(string text);

    /// <summary>
    /// Sets the displayed text for a given slot. Called whenever that slot's binding, capture prompt,
    /// or in-progress capture changes.
    /// </summary>
    protected abstract void UpdateSlotText(int slotIndex, string text);

    /// <summary>
    /// Called when a slot enters capture mode. Override to apply a "listening" visual state.
    /// </summary>
    protected virtual void OnCaptureStarted(int slotIndex) { }

    /// <summary>
    /// Called when a slot leaves capture mode (finished or cancelled). Override to revert visuals.
    /// </summary>
    protected virtual void OnCaptureEnded(int slotIndex) { }
}
