// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Sakura.Framework.Platform;

namespace Sakura.Framework.IO;

/// <summary>
/// A generic content-addressed disk cache backed by a <see cref="Storage"/>.
/// </summary>
public class DiskCache
{
    private readonly Storage storage;
    private static readonly Lock write_lock = new Lock();

    public DiskCache(Storage storage)
    {
        this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    /// <summary>
    /// Computes a hex MD5 hash of a UTF-8 string, used to build cache keys.
    /// MD5 is used purely for content addressing (not security), matching the
    /// framework's existing glyph-store hashing.
    /// </summary>
    public static string HashString(string input)
    {
        byte[] bytes = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Computes a hex MD5 hash of a byte array,used to build cache keys and payload checksums.
    /// </summary>
    public static string HashBytes(byte[] input)
    {
        byte[] bytes = MD5.HashData(input);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Tries to read a previously cached entry.
    /// </summary>
    /// <param name="key">The cache key (a filename within the backing storage).</param>
    /// <param name="data">The cached payload on success; <c>null</c> on miss.</param>
    /// <returns>true on a verified hit, false on miss, corruption, or any I/O error.</returns>
    public bool TryRead(string key, out byte[] data)
    {
        try
        {
            if (!storage.Exists(key))
            {
                data = null;
                return false;
            }

            using Stream stream = storage.GetStream(key, FileAccess.Read, FileMode.Open);
            using var reader = new BinaryReader(stream);

            string storedChecksum = reader.ReadString();
            byte[] payload = reader.ReadBytes((int)(stream.Length - stream.Position));

            // Verify integrity — a truncated or tampered file fails here and is treated as a miss.
            if (storedChecksum != HashBytes(payload))
            {
                data = null;
                return false;
            }

            data = payload;
            return true;
        }
        catch
        {
            data = null;
            return false;
        }
    }

    /// <summary>
    /// Writes data to the cache under the given key. Atomically replaces any existing entry
    /// (via <see cref="Storage.CreateFileSafely"/>) and is safe to call from multiple threads.
    /// </summary>
    public void Write(string key, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        lock (write_lock)
        {
            string checksum = HashBytes(data);

            using Stream stream = storage.CreateFileSafely(key);
            using var writer = new BinaryWriter(stream);
            writer.Write(checksum);
            writer.Write(data);
        }
    }

    /// <summary>
    /// Reads a cached UTF-8 string pair (e.g. a vertex + fragment shader pair).
    /// </summary>
    public bool TryReadStrings(string key, out string a, out string b)
    {
        if (!TryRead(key, out byte[] data))
        {
            a = b = null;
            return false;
        }

        try
        {
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);
            a = reader.ReadString();
            b = reader.ReadString();
            return true;
        }
        catch
        {
            a = b = null;
            return false;
        }
    }

    /// <summary>
    /// Writes a UTF-8 string pair to the cache.
    /// </summary>
    public void WriteStrings(string key, string a, string b)
    {
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(a);
            writer.Write(b);
        }

        Write(key, ms.ToArray());
    }
}
