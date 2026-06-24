// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Input.Bindings;

/// <summary>
/// Persists user-overridden key bindings for an action type <typeparamref name="T"/> to
/// <see cref="Storage"/>, layering them over a set of defaults. Supports per-action reset to default
/// and exposes the merged result as an <see cref="IKeyBindingSource"/> for a
/// <see cref="KeyBindingContainer{T}"/>.
/// </summary>
/// <remarks>
/// On-disk format is one line per overridden action: <c>ActionName=Combo1;Combo2</c>, using
/// <see cref="KeyCombination.ToString"/> / <see cref="KeyCombination.Parse"/> for each combination.
/// Actions left at their default are not written, so the file only records genuine customizations.
/// </remarks>
/// <typeparam name="T">The action enum type.</typeparam>
public class KeyBindingStore<T> : IKeyBindingSource where T : struct, Enum
{
    private readonly Storage storage;
    private readonly string fileName;

    /// <summary>
    /// The default bindings, grouped by action. Actions with no user override fall back to these.
    /// </summary>
    private readonly Dictionary<T, List<KeyCombination>> defaults = new Dictionary<T, List<KeyCombination>>();

    /// <summary>
    /// User overrides, keyed by action. Presence here means the action has been customised.
    /// </summary>
    private readonly Dictionary<T, List<KeyCombination>> overrides = new Dictionary<T, List<KeyCombination>>();

    /// <summary>
    /// Fired whenever the merged binding set changes (after a set or reset). Subscribers (typically a
    /// container) should call <see cref="KeyBindingContainer{T}.ReloadMappings"/>.
    /// </summary>
    public event Action? Changed;

    public KeyBindingStore(Storage storage, IEnumerable<KeyBinding> defaultBindings, string fileName = "keybindings.ini")
    {
        this.storage = storage;
        this.fileName = fileName;

        foreach (var binding in defaultBindings)
        {
            var action = binding.GetAction<T>();
            if (!defaults.TryGetValue(action, out var list))
                defaults[action] = list = new List<KeyCombination>();
            list.Add(binding.KeyCombination);
        }

        Load();
    }

    /// <summary>
    /// The combinations currently bound to the given action (override if present, else default).
    /// </summary>
    public IReadOnlyList<KeyCombination> GetCombinations(T action)
    {
        if (overrides.TryGetValue(action, out var custom))
            return custom;
        return defaults.TryGetValue(action, out var def) ? def : Array.Empty<KeyCombination>();
    }

    /// <summary>
    /// Whether the given action has been customised away from its default.
    /// </summary>
    public bool IsOverridden(T action) => overrides.ContainsKey(action);

    /// <summary>
    /// Replaces the combinations bound to an action and persists the change.
    /// </summary>
    public void SetCombinations(T action, IEnumerable<KeyCombination> combinations)
    {
        overrides[action] = combinations.ToList();
        Save();
        Changed?.Invoke();
    }

    /// <summary>
    /// Resets a single action to its default binding and persists the change.
    /// </summary>
    public void ResetToDefault(T action)
    {
        if (overrides.Remove(action))
        {
            Save();
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Resets every action to its default binding.
    /// </summary>
    public void ResetAllToDefault()
    {
        if (overrides.Count == 0)
            return;

        overrides.Clear();
        Save();
        Changed?.Invoke();
    }

    /// <summary>
    /// The merged binding list (overrides layered over defaults) as <see cref="KeyBinding"/> objects.
    /// </summary>
    public IEnumerable<KeyBinding> GetBindings(Type containerType)
    {
        foreach (var action in allActions())
        {
            foreach (var combo in GetCombinations(action))
                yield return new KeyBinding(combo, action);
        }
    }

    private IEnumerable<T> allActions()
    {
        // Union of default and overridden actions, preserving a stable enum order.
        return defaults.Keys.Union(overrides.Keys).OrderBy(a => Convert.ToInt32(a));
    }

    public void Load()
    {
        overrides.Clear();

        if (!storage.Exists(fileName))
            return;

        try
        {
            using var stream = storage.GetStream(fileName, FileAccess.Read, FileMode.Open);
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                    continue;

                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;

                string name = line[..eq].Trim();
                string value = line[(eq + 1)..].Trim();

                if (!Enum.TryParse<T>(name, ignoreCase: true, out var action))
                    continue;

                var combos = value
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(KeyCombination.Parse)
                    .Where(c => c.Keys.Length > 0)
                    .ToList();

                overrides[action] = combos;
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"[KeyBindingStore] Failed to load '{fileName}'.", ex);
        }
    }

    public void Save()
    {
        try
        {
            using var stream = storage.GetStream(fileName, FileAccess.Write, FileMode.Create);
            using var writer = new StreamWriter(stream);

            writer.WriteLine("# Sakura key bindings. One line per customised action: ActionName=Combo1;Combo2");

            foreach (var (action, combos) in overrides.OrderBy(kv => Convert.ToInt32(kv.Key)))
            {
                string value = string.Join(';', combos.Select(c => c.ToString()));
                writer.WriteLine($"{action}={value}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"[KeyBindingStore] Failed to save '{fileName}'.", ex);
        }
    }
}
