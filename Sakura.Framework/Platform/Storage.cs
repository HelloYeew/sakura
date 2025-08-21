// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using Sakura.Framework.Extensions.ObjectExtensions;

namespace Sakura.Framework.Platform;

public abstract class Storage
{
    protected string BasePath { get; }

    protected Storage(string path, string subfolder = null)
    {
        BasePath = path;

        if (BasePath == null)
        {
            throw new InvalidOperationException($"{nameof(BasePath)} is not set correctly");
        }

        if (!string.IsNullOrEmpty(subfolder))
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                subfolder = subfolder.Replace(c.ToString(), string.Empty);
            BasePath = Path.Combine(BasePath, subfolder);
        }
    }

    /// <summary>
    /// Check whether the file exists at the specified path.
    /// </summary>
    /// <param name="path">The path to the file.</param>
    /// <returns>Whether a file exists.</returns>
    public abstract bool Exists(string path);

    /// <summary>
    /// Check whether the directory exists at the specified path.
    /// </summary>
    /// <param name="path">The path to the directory.</param>
    /// <returns>Whether a directory exists.</returns>
    public abstract bool ExistsDirectory(string path);

    /// <summary>
    /// Get the full path of a file or directory at the specified path.
    /// </summary>
    /// <param name="path">An incomplete path to a file or directory.</param>
    /// <param name="createIfNotExists">Whether to create the directory if it does not exist.</param>
    /// <returns>A full usable path to a file or directory.</returns>
    public abstract string GetFullPath(string path, bool createIfNotExists = false);

    /// <summary>
    /// Get the list of files at the specified path that match the search pattern.
    /// </summary>
    /// <param name="path">Path to the directory to search in.</param>
    /// <param name="searchPattern">The search pattern to match files against. Defaults to "*".</param>
    /// <returns>A collection of file paths that match the search pattern.</returns>
    public abstract IEnumerable<string> GetFiles(string path, string searchPattern = "*");

    /// <summary>
    /// Get a list of directories at the specified path.
    /// </summary>
    /// <param name="path">Path to the directory to search in.</param>
    /// <returns>A collection of directory paths.</returns>
    public abstract IEnumerable<string> GetDirectories(string path);

    /// <summary>
    /// Return a new <see cref="Storage"/> for a contained directory.
    /// Will create the directory if it does not exist.
    /// </summary>
    /// <param name="path">The subfolder path relative to use as a root for the new storage.</param>
    /// <returns>A more specific <see cref="Storage"/> instance for the specified directory.</returns>
    /// <exception cref="ArgumentException">Subfolder cannot be null or empty.</exception>
    public virtual Storage GetStorageForDirectory(string path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentException("Subfolder cannot be null or empty.", nameof(path));

        if (!path.EndsWith(Path.DirectorySeparatorChar))
            path += Path.DirectorySeparatorChar;

        // Create the folder if it does not exist
        string fullPath = GetFullPath(path, true);

        return (Storage)Activator.CreateInstance(GetType(), fullPath);
    }

    /// <summary>
    /// Move a file from one path to another.
    /// </summary>
    /// <param name="fromPath">The source path of the file to move.</param>
    /// <param name="toPath"> The destination path where the file should be moved.</param>
    public abstract void Move(string fromPath, string toPath);

    /// <summary>
    /// Create a new file on disk by writing to a temporary file first before move to the final destination
    /// to ensure that the file is not half-written available on the specified path.
    /// </summary>
    /// <remarks>
    /// If the target file already exists, it will be deleted before the new file is created.
    /// </remarks>
    /// <param name="path">The path of the file to create or overwrite.</param>
    /// <returns>A stream to write to the file, will only exist at the specified path once the stream is disposed.</returns>
    [Pure]
    public Stream CreateFileSafely(string path)
    {
        string temporaryPath = Path.Combine(Path.GetDirectoryName(path).AsNonNull(), $"_{Path.GetFileName(path)}_{Guid.NewGuid()}");

        return new SafeWriteStream(temporaryPath, path, this);
    }

    /// <summary>
    /// Get a stream to read or write to a file at the specified path.
    /// </summary>
    /// <param name="path">The path to the file.</param>
    /// <param name="access">The access mode for the file, defaults to <see cref="FileAccess.Read"/>.</param>
    /// <param name="mode">The file mode to use when opening the file, defaults to <see cref="FileMode.OpenOrCreate"/>.</param>
    /// <returns>A <see cref="Stream"/> to read or write to the file.</returns>
    [Pure]
    public abstract Stream GetStream(string path, FileAccess access = FileAccess.Read, FileMode mode = FileMode.OpenOrCreate);

    /// <summary>
    /// Request that a file will be opened externally, e.g. by the system's default application for that file type.
    /// </summary>
    /// <param name="filename"> The name of the file to open externally.</param>
    /// <returns>Whether the file was successfully requested to be opened externally.</returns>
    public abstract bool OpenFileExternally(string filename);

    /// <summary>
    /// Open a native file browser window to the root of this storage.
    /// </summary>
    /// <returns>Whether the file browser was successfully opened.</returns>
    public bool PresentExternally() => OpenFileExternally(string.Empty);

    /// <summary>
    /// Request to present a file externally in the platform's native file browser.
    /// </summary>
    /// <remarks>
    /// This will open the parent folder of the file and will highlight the file if possible.
    /// </remarks>
    /// <param name="filename"></param>
    /// <returns></returns>
    public abstract bool PresentFileExternally(string filename);

    /// <summary>
    /// Delete a directory at the specified path.
    /// </summary>
    /// <param name="path"> The path to the directory to delete.</param>
    public abstract void DeleteDirectory(string path);

    /// <summary>
    /// Delete a file at the specified path.
    /// </summary>
    /// <param name="path"> The path to the file to delete.</param>
    public abstract void Delete(string path);
}
