// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using FFmpeg.AutoGen;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Platform;

/// <summary>
/// Locates the shipped FFmpeg binaries and initialises <see cref="FFmpeg.AutoGen"/>'s dynamic
/// bindings. Every consumer of FFmpeg must call <see cref="EnsureInitialized"/> before its first
/// <c>ffmpeg.*</c> call.
/// </summary>
internal static class FFmpegLibrary
{
    private static readonly Lock initialisation_lock = new Lock();
    private static bool initialised;

    /// <summary>
    /// Points <see cref="ffmpeg.RootPath"/> at the runtime-specific native directory and initializes
    /// the bindings, once per process.
    /// </summary>
    public static void EnsureInitialized()
    {
        lock (initialisation_lock)
        {
            if (Volatile.Read(ref initialised))
                return;

            if (initialised)
                return;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                ffmpeg.RootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtimes", "osx", "native");
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
                ffmpeg.RootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtimes", arch, "native");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                string arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
                ffmpeg.RootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtimes", arch, "native");
            }

            Logger.Verbose($"Initialized FFmpeg with root path {ffmpeg.RootPath}");
            DynamicallyLoadedBindings.Initialize();

            Volatile.Write(ref initialised, true);
        }
    }
}
