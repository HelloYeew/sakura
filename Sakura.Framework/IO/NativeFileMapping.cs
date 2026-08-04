// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.IO;

/// <summary>
/// A read-only memory mapping of a file on disk, handed to native libraries as a plain pointer.
/// </summary>
public sealed class NativeFileMapping : INativeBytes
{
    /// <summary>
    /// Total length of every live mapping.
    /// </summary>
    private static readonly GlobalStatistic<long> stat_mapped_bytes = GlobalStatistics.Get<long>("Fonts", "Mapped Bytes");

    private static long mappedBytes;

    /// <summary>
    /// Total length of every mapping currently alive.
    /// </summary>
    public static long MappedBytes => Interlocked.Read(ref mappedBytes);

    /// <summary>
    /// Both held for this object's whole lifetime, and that is what keeps <see cref="Pointer"/> valid.
    /// </summary>
    private MemoryMappedFile? file;

    private MemoryMappedViewAccessor? view;

    private int disposed;

    public IntPtr Pointer { get; private set; }

    public long Length { get; private set; }

    /// <summary>
    /// The path this mapping was created from, for diagnostics.
    /// </summary>
    public string Path { get; }

    private NativeFileMapping(string path, MemoryMappedFile file, MemoryMappedViewAccessor view, long length)
    {
        Path = path;
        this.file = file;
        this.view = view;
        Length = length;

        // A view can start before the requested offset to land on a page boundary; PointerOffset is the
        // distance from the handle's base to the byte actually asked for. Zero for a view of a whole file,
        // but reading it makes that an observation rather than an assumption.
        Pointer = view.SafeMemoryMappedViewHandle.DangerousGetHandle() + (nint)view.PointerOffset;

        stat_mapped_bytes.Value = Interlocked.Add(ref mappedBytes, length);
    }

    /// <summary>
    /// Maps a file read-only in full.
    /// </summary>
    /// <param name="path">An absolute path to an existing file.</param>
    /// <returns>
    /// The mapping, or <c>null</c> if the file is missing, empty, or cannot be mapped — a caller that gets
    /// null should fall back to reading the bytes, since mapping is an optimization and not the contract.
    /// </returns>
    public static NativeFileMapping? CreateFrom(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        MemoryMappedFile? file = null;

        try
        {
            var info = new FileInfo(path);

            if (!info.Exists || info.Length == 0)
                return null;

            // mapName must stay null: named mappings are Windows-only and throw on Unix.
            file = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);

            var view = file.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

            return new NativeFileMapping(path, file, view, info.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Mapping can fail where a plain read would succeed (a filesystem that does not support it, a
            // file held exclusively elsewhere), so this is a fallback signal rather than an error.
            file?.Dispose();
            return null;
        }
    }

    /// <summary>
    /// Releases the mapping. The pointer must not be read past this point, so every native object built on
    /// it has to be destroyed first. Repeated calls are a no-op.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        Pointer = IntPtr.Zero;

        view?.Dispose();
        view = null;

        file?.Dispose();
        file = null;

        stat_mapped_bytes.Value = Interlocked.Add(ref mappedBytes, -Length);

        Length = 0;
    }
}
