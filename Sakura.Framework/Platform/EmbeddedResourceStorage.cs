// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Sakura.Framework.Platform;

/// <summary>
/// An implementation of <see cref="Storage"/> which retrieves files from embedded resources in an assembly.
/// </summary>
public class EmbeddedResourceStorage : Storage
{
    private readonly Assembly assembly;
    private readonly string rootNamespace; // e.g., "MyGame.Resources"
    private readonly string[] manifestResourceNames;

    /// <summary>
    /// Creates a new storage for embedded resources.
    /// </summary>
    /// <param name="assembly">The assembly containing the resources.</param>
    /// <param name="rootNamespace">The root namespace of the resources (e.g., "MyProject.Resources").</param>
    public EmbeddedResourceStorage(Assembly assembly, string rootNamespace) : base(rootNamespace)
    {
        this.assembly = assembly;
        this.rootNamespace = rootNamespace.Trim('.');
        this.manifestResourceNames = assembly.GetManifestResourceNames();
    }

    /// <summary>
    /// Converts a file path (e.g., "Tracks/music.ogg") into a manifest resource name
    /// (e.g., "MyGame.Resources.Tracks.music.ogg").
    /// </summary>
    private string toManifestName(string path)
    {
        string cleanPath = path.Replace('/', '.').Replace('\\', '.');
        return $"{rootNamespace}.{cleanPath}";
    }

    /// <summary>
    /// Converts a manifest name back to a relative path.
    /// </summary>
    private string fromManifestName(string manifestName)
    {
        // Remove root namespace and a dot
        string relativePath = manifestName.Substring(rootNamespace.Length + 1);

        // This is imperfect as it doesn't know where the "real" file extension begins,
        // but it's the best we can do.
        // e.g. "MyGame.Resources.Tracks.music.ogg" -> "Tracks.music.ogg"
        // We'd ideally want "Tracks/music.ogg"
        // For now, we'll assume the store normalizes paths.
        return relativePath.Replace('.', '/'); // This is risky if filenames have dots
    }

    public override bool Exists(string path)
    {
        string manifestName = toManifestName(path);
        return manifestResourceNames.Contains(manifestName);
    }

    public override bool ExistsDirectory(string path)
    {
        string manifestPrefix = toManifestName(path) + ".";
        return manifestResourceNames.Any(n => n.StartsWith(manifestPrefix));
    }

    public override string GetFullPath(string path, bool createIfNotExists = false)
    {
        // "Full path" in this context is the manifest resource name
        return toManifestName(path);
    }

    public override Stream? GetStream(string path, FileAccess access = FileAccess.Read, FileMode mode = FileMode.OpenOrCreate)
    {
        if (access != FileAccess.Read)
            throw new NotSupportedException("Cannot write to embedded resources.");

        if (mode == FileMode.Create || mode == FileMode.CreateNew || mode == FileMode.Truncate)
            throw new NotSupportedException("Cannot create or truncate embedded resources.");

        string manifestName = toManifestName(path);
        Stream? stream = assembly.GetManifestResourceStream(manifestName);

        if (stream == null && (mode == FileMode.Open || mode == FileMode.OpenOrCreate))
            throw new FileNotFoundException($"Embedded resource not found: {manifestName}");

        return stream;
    }

    public override IEnumerable<string> GetFiles(string path, string searchPattern = "*")
    {
        string manifestPrefix = toManifestName(path) + ".";

        return manifestResourceNames
            .Where(n => n.StartsWith(manifestPrefix))
            .Select(fromManifestName);
    }

    public override IEnumerable<string> GetDirectories(string path)
    {
        string manifestPrefix = toManifestName(path) + ".";

        return manifestResourceNames
            .Where(n => n.StartsWith(manifestPrefix))
            .Select(n => n.Substring(manifestPrefix.Length)) // Get "sub.path.file.ext"
            .Select(n => n.Split('.')[0]) // Get "sub"
            .Distinct();
    }

    public override Storage GetStorageForDirectory(string path)
    {
        string newRootNamespace = toManifestName(path);
        return new EmbeddedResourceStorage(assembly, newRootNamespace);
    }

    #region Unsupported Operations

    public override void Move(string fromPath, string toPath)
        => throw new NotSupportedException("Cannot move embedded resources.");

    public override bool OpenFileExternally(string filename)
        => false;

    public override bool PresentFileExternally(string filename)
        => false;

    public override void DeleteDirectory(string path)
        => throw new NotSupportedException("Cannot delete embedded resources.");

    public override void Delete(string path)
        => throw new NotSupportedException("Cannot delete embedded resources.");

    #endregion
}
