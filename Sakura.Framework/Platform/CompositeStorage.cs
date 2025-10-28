// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Sakura.Framework.Platform;

/// <summary>
/// Am implementation of <see cref="Storage"/> that combines multiple storage sources.
/// It checks for existence and retrieves files from storages in order.
/// </summary>
public class CompositeStorage : Storage
{
    private readonly IReadOnlyList<Storage> storages;

    public CompositeStorage(params Storage[] storages) : base(string.Join(";", storages.Select(s => s.GetFullPath(""))))
    {
        this.storages = storages;
    }

    private CompositeStorage(string basePath, IReadOnlyList<Storage> storages) : base(basePath)
    {
        this.storages = storages;
    }

    public override bool Exists(string path)
    {
        return storages.Any(s => s.Exists(path));
    }

    public override bool ExistsDirectory(string path)
    {
        return storages.Any(s => s.ExistsDirectory(path));
    }

    public override string? GetFullPath(string path, bool createIfNotExists = false)
    {
        // Return the first path found. This is ambiguous but necessary.
        return storages.FirstOrDefault(s => s.Exists(path))?.GetFullPath(path, createIfNotExists)
            ?? storages.FirstOrDefault()?.GetFullPath(path, createIfNotExists);
    }

    public override Stream? GetStream(string path, FileAccess access = FileAccess.Read, FileMode mode = FileMode.OpenOrCreate)
    {
        // For read access, find the first storage that has the file.
        if (access == FileAccess.Read)
        {
            foreach (var storage in storages)
            {
                if (storage.Exists(path))
                {
                    return storage.GetStream(path, access, mode);
                }
            }

            // If not found, and we're allowed to create, use the first storage (primary).
            if (mode == FileMode.OpenOrCreate || mode == FileMode.Open)
                return storages.FirstOrDefault()?.GetStream(path, access, mode);
        }

        // For write access, always use the *first* storage (primary storage).
        if (access == FileAccess.Write || access == FileAccess.ReadWrite)
        {
            return storages.FirstOrDefault()?.GetStream(path, access, mode);
        }

        throw new FileNotFoundException($"Could not find file '{path}' in any storage.");
    }

    public override IEnumerable<string> GetFiles(string path, string searchPattern = "*")
    {
        // Return a distinct list of all files from all storages.
        return storages.SelectMany(s => s.GetFiles(path, searchPattern)).Distinct();
    }

    public override IEnumerable<string> GetDirectories(string path)
    {
        return storages.SelectMany(s => s.GetDirectories(path)).Distinct();
    }

    public override Storage GetStorageForDirectory(string path)
    {
        // Create a new composite storage where each child is also for that directory.
        var newChildren = storages.Select(s => s.GetStorageForDirectory(path)).ToList();
        string newBasePath = string.Join(";", newChildren.Select(s => s.GetFullPath("")));
        return new CompositeStorage(newBasePath, newChildren);
    }

    // Write or delete operation should use the primary (first) storage only.

    public override void Move(string fromPath, string toPath)
        => storages.FirstOrDefault()?.Move(fromPath, toPath);

    public override bool OpenFileExternally(string filename)
        => storages.FirstOrDefault(s => s.Exists(filename))?.OpenFileExternally(filename) ?? false;

    public override bool PresentFileExternally(string filename)
        => storages.FirstOrDefault(s => s.Exists(filename))?.PresentFileExternally(filename) ?? false;

    public override void DeleteDirectory(string path)
        => storages.FirstOrDefault()?.DeleteDirectory(path);

    public override void Delete(string path)
        => storages.FirstOrDefault()?.Delete(path);
}
