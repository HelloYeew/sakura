// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input.Bindings;

namespace Sakura.Framework.Graphics.UserInterface.KeyBinding;

public abstract partial class KeyBindingPanel<T> : Container where T : struct, Enum
{
    private readonly KeyBindingStore<T> store;

    /// <summary>
    /// Height in pixels of each binding row.
    /// </summary>
    public float RowHeight { get; init; } = 32;

    /// <summary>
    /// Vertical gap in pixels between rows.
    /// </summary>
    public float RowSpacing { get; init; } = 2;

    /// <summary>
    /// Number of binding slots shown per action
    /// </summary>
    public int SlotsPerAction { get; init; } = 1;

    protected KeyBindingPanel(KeyBindingStore<T> store)
    {
        this.store = store;

        // Fill the available width, but grow our height to contain the manually-stacked rows.
        // We must NOT be relative on Y here: rows are positioned in absolute pixels (Y = 0, 32, ...),
        // so the panel needs a real pixel height for them to fall inside its bounds (and thus be
        // hit-testable). A relative height would resolve to the parent's pixel height — or, if the
        // parent doesn't supply one, to ~1px, leaving every row but the first outside the panel.
        RelativeSizeAxes = Axes.X;
        Width = 1;
        AutoSizeAxes = Axes.Y;
    }

    public override void LoadComplete()
    {
        base.LoadComplete();
        rebuild();
    }

    private void rebuild()
    {
        Clear();

        float y = 0;

        foreach (var action in Enum.GetValues(typeof(T)).Cast<T>())
        {
            var combos = store.GetCombinations(action);

            var row = CreateRow(action, combos);
            row.Y = y;
            row.Height = RowHeight;
            row.Changed += onRowChanged;
            Add(row);

            y += RowHeight + RowSpacing;
        }
    }

    private void onRowChanged(T action, IReadOnlyList<KeyCombination> combos)
    {
        // Persist all non-empty slots; empty (unbound) slots are dropped so the store records only
        // real bindings.
        store.SetCombinations(action, combos.Where(c => c.Keys.Length > 0));
    }

    /// <summary>
    /// Resets every action to its default binding and refreshes the displayed rows.
    /// </summary>
    public void ResetAll()
    {
        store.ResetAllToDefault();
        rebuild();
    }

    /// <summary>
    /// Creates the row drawable for an action and its current combinations. Override to supply custom
    /// row visuals. Implementations should size the row to <see cref="SlotsPerAction"/> slots.
    /// </summary>
    protected abstract KeyBindingRow<T> CreateRow(T action, IReadOnlyList<KeyCombination> current);
}
