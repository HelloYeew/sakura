// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using System.Runtime.InteropServices;
using ManagedBass;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Audio.BassEngine;

/// <summary>
/// Utility class for BASS-related operations.
/// </summary>
internal static class BassUtils
{
    /// <summary>
    /// Checks the result of a BASS function call and logs an error if it failed.
    /// </summary>
    /// <param name="result">The result of the BASS function (true for success, false for failure).</param>
    /// <param name="message">The operation that was being attempted.</param>
    /// <returns>True if successful, false if failed.</returns>
    public static bool CheckError(bool result, string message)
    {
        if (!result)
        {
            Logger.Error($"BASS Error: {Bass.LastError} while {message}", new BassException(Bass.LastError));
            return false;
        }
        return true;
    }

    /// <summary>
    /// Checks the result of a BASS function call that returns a handle (int).
    /// </summary>
    /// <param name="handle">The handle returned by BASS. 0 indicates an error.</param>
    /// <param name="message">The operation that was being attempted.</param>
    /// <returns>The handle if successful, otherwise 0.</returns>
    public static int CheckError(int handle, string message)
    {
        if (handle == 0)
        {
            Logger.Error($"BASS Error: {Bass.LastError} while {message}", new BassException(Bass.LastError));
            return 0;
        }
        return handle;
    }

    /// <summary>
    /// Helper to read a Stream into an unmanaged memory buffer for BASS.
    /// </summary>
    public static IntPtr StreamToUnmanagedMemory(Stream stream)
    {
        if (stream == null) return IntPtr.Zero;

        if (stream is MemoryStream ms)
        {
            byte[] buffer = ms.GetBuffer();
            IntPtr ptr = Marshal.AllocHGlobal((int)ms.Length);
            Marshal.Copy(buffer, 0, ptr, (int)ms.Length);
            return ptr;
        }
        else
        {
            // Fallback for other stream types
            using (var memoryStream = new MemoryStream())
            {
                stream.CopyTo(memoryStream);
                var buffer = memoryStream.GetBuffer();
                IntPtr ptr = Marshal.AllocHGlobal((int)memoryStream.Length);
                Marshal.Copy(buffer, 0, ptr, (int)memoryStream.Length);
                return ptr;
            }
        }
    }
}
