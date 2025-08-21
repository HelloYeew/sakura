// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sakura.Framework.Extensions.ObjectExtensions;
using Sakura.Framework.Utilities;

namespace Sakura.Framework.Platform;

public class NativeStorage : Storage
{
    private readonly AppHost host;

    public NativeStorage(string path, AppHost host = null) : base(path)
    {
        this.host = host;
    }

    public override bool Exists(string path) => File.Exists(GetFullPath(path));

    public override bool ExistsDirectory(string path) => Directory.Exists(GetFullPath(path));

    public override void DeleteDirectory(string path)
    {
        path = GetFullPath(path);

        if (Directory.Exists(path))
            Directory.Delete(path, true);
    }

    public override void Delete(string path)
    {
        path = GetFullPath(path);

        if (File.Exists(path))
            File.Delete(path);
    }

    public override void Move(string fromPath, string toPath)
    {
        // Retry since maybe on windows the file is being used by another process.
        General.AttemptWithRetryOnException<IOException>(() => File.Move(GetFullPath(fromPath), GetFullPath(toPath), true));
    }

    public override IEnumerable<string> GetDirectories(string path) => getRelativePaths(Directory.GetDirectories(GetFullPath(path)));

    public override IEnumerable<string> GetFiles(string path, string searchPattern = "*") => getRelativePaths(Directory.GetFiles(GetFullPath(path), searchPattern));

    public override string GetFullPath(string path, bool createIfNotExists = false)
    {
        path = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

        string basePath = Path.GetFullPath(BasePath).TrimEnd(Path.DirectorySeparatorChar);
        string resolvedPath = Path.GetFullPath(Path.Combine(basePath, path));

        if (!resolvedPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Resolved path '{resolvedPath}' is not under the base path '{basePath}'.");

        if (createIfNotExists)
            Directory.CreateDirectory(Path.GetDirectoryName(resolvedPath).AsNonNull());

        return resolvedPath;
    }

    public override bool OpenFileExternally(string filename) => host?.OpenFileExternally(GetFullPath(filename)) == true;

    public override bool PresentFileExternally(string filename) => host?.PresentFileExternally(GetFullPath(filename)) == true;

    public override Stream GetStream(string path, FileAccess access = FileAccess.Read, FileMode mode = FileMode.OpenOrCreate)
    {
        path = GetFullPath(path, access != FileAccess.Read);

        ArgumentException.ThrowIfNullOrEmpty(path);

        switch (access)
        {
            case FileAccess.Read:
                if (!File.Exists(path))
                    return null;

                return File.Open(path, FileMode.Open, access, FileShare.Read);

            default:
                // this was added to work around some hardware writing zeroes to a file
                // before writing actual content, causing corrupt files to exist on disk.
                // as of .NET 6, flushing is very expensive on macOS so this is limited to only Windows.
                return RuntimeInfo.OS == RuntimeInfo.Platform.Windows ? new FlushingStream(path, mode, access) : new FileStream(path, mode, access);
        }
    }

    public override Storage GetStorageForDirectory(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (path.Length > 0 && !path.EndsWith(Path.DirectorySeparatorChar))
            path += Path.DirectorySeparatorChar;

        // Make sure that the path is existing and valid by creating the directory if it does not exist.
        string fullPath = GetFullPath(path, true);

        return (Storage)Activator.CreateInstance(GetType(), fullPath, host);
    }

    private IEnumerable<string> getRelativePaths(IEnumerable<string> paths)
    {
        string basePath = Path.GetFullPath(GetFullPath(string.Empty));

        return paths
            .Select(Path.GetFullPath)
            .Select(path =>
            {
                if (!path.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"Path '{path}' is not under the base path '{basePath}'.");

                return path.AsSpan(basePath.Length).TrimStart(Path.DirectorySeparatorChar).ToString();
            });
    }
}
