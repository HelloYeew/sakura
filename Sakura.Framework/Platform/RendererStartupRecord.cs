// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Platform;

/// <summary>
/// Keeps, next to <c>framework.ini</c>, the renderers that were starting when the app last stopped without
/// warning. A driver that crashes the process while it starts leaves no exception for the host to fall back
/// on, so without this the same renderer would be chosen, and crash, on every launch, before the player could
/// choose another. A renderer found here is tried after the others until it starts, or is chosen in the
/// settings again.
/// </summary>
internal class RendererStartupRecord
{
    public const string FILENAME = "renderer-crashes.txt";

    private readonly Storage storage;
    private readonly List<RendererType> crashed;
    private readonly Lock writeLock = new Lock();

    /// <summary>
    /// The renderers that were starting when the app last stopped without warning, in the order they did.
    /// </summary>
    public IReadOnlyList<RendererType> Crashed => crashed;

    public RendererStartupRecord(Storage storage)
    {
        this.storage = storage;
        crashed = read(storage);
    }

    /// <summary>
    /// Moves the renderers in <see cref="Crashed"/> to the end of <paramref name="candidates"/>, keeping the
    /// order of the rest. They stay in the list, so a platform with a single renderer still tries it.
    /// </summary>
    public void MoveCrashedLast(List<RendererType> candidates)
    {
        var last = candidates.Where(crashed.Contains).ToList();
        candidates.RemoveAll(crashed.Contains);
        candidates.AddRange(last);
    }

    /// <summary>
    /// Records that <paramref name="type"/> is starting, before any of it runs. Until <see cref="Started"/>
    /// or <see cref="Failed"/>, the app stopping counts against it.
    /// </summary>
    public void Starting(RendererType type)
    {
        lock (writeLock)
            write(crashed.Contains(type) ? crashed : crashed.Append(type));
    }

    /// <summary>
    /// Records that <paramref name="type"/> started, which clears it if it had crashed before.
    /// </summary>
    public void Started(RendererType type)
    {
        lock (writeLock)
        {
            crashed.Remove(type);
            write(crashed);
        }
    }

    /// <summary>
    /// Records that the renderer last <see cref="Starting"/> failed in a way the host caught, which is no
    /// crash.
    /// </summary>
    public void Failed()
    {
        lock (writeLock)
            write(crashed);
    }

    /// <summary>
    /// Gives <paramref name="type"/> another chance, as when the player chooses it in the settings.
    /// </summary>
    public void Forgive(RendererType type)
    {
        lock (writeLock)
        {
            if (crashed.Remove(type))
                write(crashed);
        }
    }

    private static List<RendererType> read(Storage storage)
    {
        var types = new List<RendererType>();

        try
        {
            if (!storage.Exists(FILENAME))
                return types;

            using var reader = new StreamReader(storage.GetStream(FILENAME));

            while (reader.ReadLine() is string line)
            {
                if (Enum.TryParse(line.Trim(), out RendererType type) && type != RendererType.Automatic && !types.Contains(type))
                    types.Add(type);
            }
        }
        catch (Exception e)
        {
            Logger.Warning($"Couldn't read {FILENAME}, so no renderer counts as having crashed: {e.Message}");
        }

        return types;
    }

    private void write(IEnumerable<RendererType> types)
    {
        try
        {
            var lines = types.Select(t => t.ToString()).ToList();

            if (lines.Count == 0)
            {
                if (storage.Exists(FILENAME))
                    storage.Delete(FILENAME);

                return;
            }

            using var writer = new StreamWriter(storage.CreateFileSafely(FILENAME));

            foreach (string line in lines)
                writer.WriteLine(line);
        }
        catch (Exception e)
        {
            Logger.Warning($"Couldn't write {FILENAME}, so a renderer crashing now wouldn't be noticed next launch: {e.Message}");
        }
    }
}
