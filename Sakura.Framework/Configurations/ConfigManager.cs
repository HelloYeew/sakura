// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
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
    /// <summary>
    /// How long to wait after a change before writing to disk, to collapse bursts of changes into one write.
    /// </summary>
    private const int save_debounce_ms = 200;

    private readonly Storage? storage;
    private readonly string fileName;
    private readonly Dictionary<TLookup, object> settings = new Dictionary<TLookup, object>();

    /// <summary>
    /// Values read from the backing file for settings that have not been registered via <see cref="Get{TValue}"/> yet.
    /// These are applied when the setting is eventually registered, and written back out in the meantime so that
    /// a partially-initialised manager can never drop settings it doesn't know about.
    /// </summary>
    private readonly Dictionary<TLookup, string> unclaimedValues = new Dictionary<TLookup, string>();

    private readonly Lock mutex = new Lock();

    private Task? saveTask;
    private bool loading;

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
        lock (mutex)
        {
            if (settings.TryGetValue(lookup, out object? existing))
            {
                if (existing is Reactive<TValue> existingTyped)
                    return existingTyped;

                throw new InvalidCastException(
                    $"Setting '{lookup}' is of type '{existing.GetType().GetGenericArguments()[0]}' but was requested as '{typeof(TValue)}'. "
                    + $"If those two types are unrelated, the assembly declaring {typeof(TLookup).Name} is likely stale relative to its callers "
                    + "(a member added or removed in the middle of the enum shifts every value after it) — do a clean rebuild of all projects.");
            }

            var reactive = new Reactive<TValue>(defaultValue);

            // apply any value that was read from disk before this setting was registered.
            if (unclaimedValues.Remove(lookup, out string? unclaimed))
                parseInto(lookup, reactive, unclaimed);

            reactive.ValueChanged += _ =>
            {
                Logger.Debug($"[{GetType().Name}] Setting '{lookup}' changed to '{reactive.Value}'.");
                Save();
            };

            settings[lookup] = reactive;

            return reactive;
        }
    }

    /// <summary>
    /// Whether <paramref name="lookup"/> has had a default registered via <see cref="Get{TValue}"/>.
    /// </summary>
    public bool IsRegistered(TLookup lookup)
    {
        lock (mutex)
            return settings.ContainsKey(lookup);
    }

    /// <summary>
    /// Load settings from the backing file.
    /// </summary>
    public virtual void Load()
    {
        if (storage == null)
            return;

        if (!storage.Exists(fileName))
        {
            performSave();
            return;
        }

        bool needsRewrite = false;

        var present = new HashSet<TLookup>();

        lock (mutex)
        {
            // suppress the save each parsed value would otherwise schedule; a single writing happens below if needed.
            loading = true;

            try
            {
                using var stream = storage.GetStream(fileName);

                if (stream == null)
                    return;

                using var reader = new StreamReader(stream);

                string? line;

                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    string[] parts = line.Split('=', 2);

                    if (parts.Length != 2)
                    {
                        Logger.Warning($"[{GetType().Name}] Ignoring malformed line in {fileName}: '{line}'.");
                        needsRewrite = true;
                        continue;
                    }

                    string key = parts[0].Trim();
                    string value = parts[1].Trim();

                    // Enum.TryParse also accepts raw numeric strings, which would happily produce an out-of-range
                    // member, so the key has to be checked against the declared members as well.
                    if (!Enum.TryParse<TLookup>(key, out var lookup) || !Enum.IsDefined(lookup))
                    {
                        Logger.Warning($"[{GetType().Name}] Ignoring unknown setting '{key}' in {fileName}.");
                        needsRewrite = true;
                        continue;
                    }

                    present.Add(lookup);

                    if (settings.TryGetValue(lookup, out object? reactive))
                    {
                        if (!parseInto(lookup, reactive, value))
                            needsRewrite = true;
                    }
                    else
                    {
                        // registered later by a Get() call, which will pick this up.
                        unclaimedValues[lookup] = value;
                    }
                }

                if (settings.Keys.Any(setting => !present.Contains(setting)))
                    needsRewrite = true;
            }
            finally
            {
                loading = false;
            }
        }

        if (needsRewrite)
            performSave();
    }

    /// <summary>
    /// Applies a string value read from the backing file to a <see cref="Reactive{T}"/>.
    /// </summary>
    /// <returns>Whether the value was applied successfully.</returns>
    private bool parseInto(TLookup lookup, object reactive, string value)
    {
        try
        {
            var parseMethod = reactive.GetType().GetMethod("Parse");

            if (parseMethod == null)
            {
                Logger.Warning($"[{GetType().Name}] Setting '{lookup}' has no Parse method; keeping its default.");
                return false;
            }

            parseMethod.Invoke(reactive, new object[] { value, CultureInfo.InvariantCulture });
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warning($"[{GetType().Name}] Failed to parse setting '{lookup}' from value '{value}'. Falling back to default. Error: {ex.InnerException?.Message ?? ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Schedule a save operation. This is debounced to avoid excessive disk writes.
    /// </summary>
    public virtual void Save()
    {
        if (storage == null)
            return;

        lock (mutex)
        {
            if (loading)
                return;

            // an in-flight save reads the current values when it runs, so it already covers this change.
            if (saveTask?.IsCompleted == false)
                return;

            saveTask = Task.Run(async () =>
            {
                await Task.Delay(save_debounce_ms).ConfigureAwait(false);
                performSave();
            });
        }
    }

    /// <summary>
    /// Write any outstanding changes to disk immediately, waiting for a debounced <see cref="Save"/> to settle first.
    /// Call this on shutdown, otherwise changes made within the debounce window are lost.
    /// </summary>
    public void Flush()
    {
        if (storage == null)
            return;

        Task? pending;

        lock (mutex)
            pending = saveTask;

        try
        {
            pending?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            Logger.Warning($"[{GetType().Name}] A pending save failed before flush: {ex.InnerException?.Message ?? ex.Message}");
        }

        performSave();
    }

    private void performSave()
    {
        if (storage == null)
            return;

        try
        {
            lock (mutex)
            {
                // FileMode.Create rather than the storage default of OpenOrCreate: without truncation, writing a file
                // shorter than the one already on disk leaves the tail of the old content behind, producing garbage
                // lines like "rsorSensitivity = 1" after the real settings.
                using var stream = storage.GetStream(fileName, FileAccess.Write, FileMode.Create);
                using var writer = new StreamWriter(stream);

                var lines = settings
                    .Select(kvp => (kvp.Key, Value: format(kvp.Value)))
                    // settings that were never registered this run are preserved as-is rather than dropped.
                    .Concat(unclaimedValues.Select(kvp => (kvp.Key, kvp.Value)))
                    .OrderBy(pair => pair.Key);

                foreach ((var key, string value) in lines)
                    writer.WriteLine($"{key} = {value}");
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"[{GetType().Name}] Failed to write {fileName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Formats a setting's value for the backing file. Values are always written with the invariant culture, since
    /// that is what they are parsed back with.
    /// </summary>
    private static string format(object reactive)
    {
        object? value = reactive.GetType().GetProperty("Value")?.GetValue(reactive);

        return value switch
        {
            null => string.Empty,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }
}
