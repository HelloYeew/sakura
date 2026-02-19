// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Configurations;

/// <summary>
/// A manager for handling application configurations.
/// </summary>
/// <typeparam name="TLookup"></typeparam>
public abstract class ConfigManager<TLookup> where TLookup : struct, Enum
{
    private readonly Storage? storage;
    private readonly string fileName;
    private readonly Dictionary<TLookup, object> settings = new();

    private Task? saveTask;

    protected ConfigManager(Storage? storage)
    {
        this.storage = storage;

        if (storage != null)
        {
            var attribute = typeof(TLookup).GetCustomAttribute<SettingSourceAttribute>();
            if (attribute == null)
                throw new InvalidOperationException($"The enum type {typeof(TLookup).Name} must have a {nameof(SettingSourceAttribute)}.");

            fileName = attribute.FileName;
        }
    }

    /// <summary>
    /// Retrieves a <see cref="Reactive{T}"/> setting. If the setting does not exist, it is created with the provided default value.
    /// </summary>
    /// <param name="lookup">The setting to retrieve.</param>
    /// <param name="defaultValue">The default value if the setting doesn't exist.</param>
    /// <typeparam name="TValue">The type of the setting's value.</typeparam>
    /// <returns>A <see cref="Reactive{T}"/> representing the setting.</returns>
    /// <exception cref="InvalidCastException">Thrown if the existing setting's type does not match the requested type.</exception>
    public Reactive<TValue> Get<TValue>(TLookup lookup, TValue defaultValue = default)
    {
        if (settings.TryGetValue(lookup, out var existing))
        {
            if (existing is Reactive<TValue> existingTyped)
                return existingTyped;

            throw new InvalidCastException($"Setting '{lookup}' is of type '{existing.GetType().GetGenericArguments()[0]}' but was requested as '{typeof(TValue)}'.");
        }

        var reactive = new Reactive<TValue>(defaultValue);
        reactive.ValueChanged += _ => Save();
        reactive.ValueChanged += _ => Logger.Debug($"[{GetType().Name}] Setting '{lookup}' changed to '{reactive.Value}'.");
        settings[lookup] = reactive;

        return reactive;
    }

    /// <summary>
    /// Load settings from the backing file.
    /// </summary>
    public virtual void Load()
    {
        if (storage == null || !storage.Exists(fileName))
            return;

        if (storage.Exists(fileName))
        {
            using var stream = storage.GetStream(fileName);
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                string[] parts = line.Split('=', 2);
                if (parts.Length != 2)
                    continue;

                string key = parts[0].Trim();
                string value = parts[1].Trim();

                if (Enum.TryParse<TLookup>(key, out var lookup))
                {
                    // Find the existing reactive object (which has the correct generic type)
                    if (settings.TryGetValue(lookup, out var reactiveObject))
                    {
                        // Use reflection to call its Parse method
                        var parseMethod = reactiveObject.GetType().GetMethod("Parse");
                        parseMethod?.Invoke(reactiveObject, new object[] { value, System.Globalization.CultureInfo.InvariantCulture });
                    }
                }
            }
        }
        else
        {
            performSave();
        }
    }

    /// <summary>
    /// Schedule a save operation. This is debounced to avoid excessive disk writes.
    /// </summary>
    public virtual void Save()
    {
        if (saveTask?.IsCompleted == false)
            return;

        saveTask = Task.Run(async () =>
        {
            await Task.Delay(200); // Debounce saves
            performSave();
        });
    }

    private void performSave()
    {
        if (storage == null) return;

        using var stream = storage.GetStream(fileName, FileAccess.Write);
        using var writer = new StreamWriter(stream);

        foreach (var (key, reactive) in settings.OrderBy(kvp => kvp.Key))
        {
            var valueProperty = reactive.GetType().GetProperty("Value");
            object? value = valueProperty?.GetValue(reactive);

            writer.WriteLine($"{key} = {value}");
        }
    }
}
