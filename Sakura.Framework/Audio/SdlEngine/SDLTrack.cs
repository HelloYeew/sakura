// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using Sakura.Framework.IO;
using Sakura.Framework.Logging;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// SDL implementation of <see cref="ITrack"/>. Tracks stream: each channel decodes independently
/// of the encoded source, so nothing holds a whole song as float PCM.
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
internal sealed unsafe class SDLTrack : ITrack, IHasActiveChannels, IDisposable
{
    private readonly SDLAudioManager manager;
    private readonly string? filePath;
    private readonly NativeMemoryBuffer? data;

    private int activeChannelCount;
    private bool isDisposed;

    public double Length { get; }

    /// <summary>
    /// Where looping playback restarts from, in milliseconds. Applied to channels this track creates.
    /// </summary>
    public double RestartPoint { get; set; }

    public bool HasActiveChannels => Volatile.Read(ref activeChannelCount) > 0;

    public SDLTrack(SDLAudioManager manager, string path)
    {
        this.manager = manager;
        filePath = path;

        Length = probeLength();

        if (Length > 0)
        {
            GlobalStatistics.Get<int>("Audio", "Loaded Tracks").Value++;
            Logger.Debug($"🔈 Track opened from file, no in-memory copy: {path}");
        }
    }

    public SDLTrack(SDLAudioManager manager, Stream stream)
    {
        this.manager = manager;

        data = NativeMemoryBuffer.CreateFrom(stream, NativeMemoryCategory.Audio);

        if (data == null)
        {
            Logger.Error("Refusing to create a track from an empty stream.", new InvalidDataException("Audio stream contained no data."));
            return;
        }

        Length = probeLength();

        if (Length > 0)
        {
            GlobalStatistics.Get<int>("Audio", "Loaded Tracks").Value++;
            Logger.Verbose($"🔈 Track loaded from stream ({data.Length / 1024} KB held in unmanaged memory)");
        }
    }

    /// <summary>
    /// Reads just enough of the source to learn its duration. Header parsing only — no audio is
    /// decoded and nothing is retained.
    /// </summary>
    private double probeLength()
    {
        try
        {
            var source = openEncodedStream();

            if (source == null)
                return 0;

            using var decoder = new FFmpegAudioDecoder(source);
            return decoder.Duration;
        }
        catch (Exception e)
        {
            Logger.Error($"Could not read the duration of {filePath ?? "an audio stream"}.", e);
            return 0;
        }
    }

    /// <summary>
    /// Opens an independent read over the encoded audio, for one decoder to own.
    /// </summary>
    private Stream? openEncodedStream()
    {
        if (filePath != null)
            return File.OpenRead(filePath);

        if (data == null)
            return null;

        return new UnmanagedMemoryStream((byte*)data.Pointer, data.Length);
    }

    public IAudioChannel GetChannel()
    {
        if (isDisposed || Length <= 0)
            return null!;

        // Held for as long as the channel lives. Without it an eviction between here and the
        // channel's disposal would free the encoded bytes underneath a live decoder.
        bool holdsReference = false;

        try
        {
            if (data != null)
            {
                holdsReference = data.AddReference();

                if (!holdsReference)
                    return null!;
            }

            var stream = openEncodedStream();

            if (stream == null)
            {
                if (holdsReference) data.Release();
                return null!;
            }

            // Both engines stream a track: the difference is only whose ring buffer the decode thread
            // fills. The native one is drained by the device callback with no managed code involved.
            var channel = manager.UsesNativeMixEngine
                ? manager.CreateNativeStreamingChannel(stream, manager.TrackMixer)
                : manager.CreateChannel(new StreamingPcmSource(stream, manager.SampleRate, manager.Channels), manager.TrackMixer, streaming: true);

            if (channel == null)
            {
                if (holdsReference) data!.Release();
                return null!;
            }

            Interlocked.Increment(ref activeChannelCount);

            bool releaseOnDispose = holdsReference;

            channel.Disposed += () =>
            {
                Interlocked.Decrement(ref activeChannelCount);

                if (releaseOnDispose)
                    data!.Release();
            };

            // Tracks loop by default, matching the BASS backend.
            channel.Looping = true;
            channel.RestartPoint = RestartPoint;

            return channel;
        }
        catch (Exception e)
        {
            if (holdsReference)
                data!.Release();

            Logger.Error($"Could not create a playback channel for {filePath ?? "an audio stream"}.", e);
            return null!;
        }
    }

    public void Dispose()
    {
        if (isDisposed)
            return;

        isDisposed = true;

        if (Length > 0)
            GlobalStatistics.Get<int>("Audio", "Loaded Tracks").Value--;

        // Only this track's own reference. Channels still playing hold theirs, and the block
        // survives until the last of them is disposed.
        data?.Release();
    }
}
