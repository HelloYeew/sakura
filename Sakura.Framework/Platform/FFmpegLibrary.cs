// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
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

    /// <summary>
    /// Which of the audio formats the framework expects to handle are actually decodable by the
    /// shipped FFmpeg build.
    /// </summary>
    public static AudioDecoderSupport GetAudioDecoderSupport()
    {
        EnsureInitialized();

        (string Name, AVCodecID Id)[] expected =
        [
            ("mp3", AVCodecID.AV_CODEC_ID_MP3),
            ("vorbis", AVCodecID.AV_CODEC_ID_VORBIS),
            ("flac", AVCodecID.AV_CODEC_ID_FLAC),
            ("aac", AVCodecID.AV_CODEC_ID_AAC),
            ("alac", AVCodecID.AV_CODEC_ID_ALAC),
            ("opus", AVCodecID.AV_CODEC_ID_OPUS),
            ("pcm_s16le", AVCodecID.AV_CODEC_ID_PCM_S16LE)
        ];

        var present = new List<string>();
        var missing = new List<string>();

        foreach (var (name, id) in expected)
        {
            unsafe
            {
                (ffmpeg.avcodec_find_decoder(id) != null ? present : missing).Add(name);
            }
        }

        return new AudioDecoderSupport(present, missing);
    }

    /// <summary>
    /// Version numbers of the loaded FFmpeg libraries for logging.
    /// </summary>
    public static string DescribeVersions()
    {
        EnsureInitialized();

        return $"avcodec {version(ffmpeg.avcodec_version())}, " +
               $"avformat {version(ffmpeg.avformat_version())}, " +
               $"avutil {version(ffmpeg.avutil_version())} ({ffmpeg.av_version_info()})";

        static string version(uint packed) => $"{packed >> 16 & 0xFF}.{packed >> 8 & 0xFF}.{packed & 0xFF}";
    }
}

/// <summary>
/// The audio formats the shipped FFmpeg build can and cannot decode.
/// </summary>
internal readonly record struct AudioDecoderSupport(IReadOnlyList<string> Present, IReadOnlyList<string> Missing);
