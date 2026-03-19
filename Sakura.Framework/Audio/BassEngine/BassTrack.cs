// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using System.Runtime.InteropServices;
using ManagedBass;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Audio.BassEngine;

/// <summary>
/// BASS implementation of <see cref="ITrack"/>
/// </summary>
internal class BassTrack : ITrack
{
    private readonly BassAudioManager manager;

    private readonly string filePath;
    private readonly System.IntPtr dataPtr;
    private readonly long dataLength;
    private GCHandle dataHandle;

    private readonly int decoderStreamHandle;

    public double Length { get; }
    public double RestartPoint { get; set; }

    /// <summary>
    /// Creates a track from a stream.
    /// </summary>
    public BassTrack(BassAudioManager manager, Stream stream)
    {
        this.manager = manager;
        filePath = null;

        using (var ms = new MemoryStream())
        {
            stream.CopyTo(ms);
            byte[] data = ms.ToArray();
            dataLength = data.Length;
            dataHandle = GCHandle.Alloc(data, GCHandleType.Pinned);
            dataPtr = dataHandle.AddrOfPinnedObject();

            decoderStreamHandle = Bass.CreateStream(dataPtr, 0, dataLength, BassFlags.Decode | BassFlags.Prescan);
        }

        if (decoderStreamHandle == 0)
        {
            Logger.Error($"BASS Error: {Bass.LastError} while creating track from stream.",
                new BassException(Bass.LastError));
            return;
        }

        Length = Bass.ChannelBytes2Seconds(decoderStreamHandle, Bass.ChannelGetLength(decoderStreamHandle)) * 1000.0;
    }

    /// <summary>
    /// Creates a track from a file path.
    /// </summary>
    public BassTrack(BassAudioManager manager, string path)
    {
        this.manager = manager;
        filePath = path; // Mark as file-based
        dataPtr = IntPtr.Zero;

        decoderStreamHandle = Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Prescan);

        if (decoderStreamHandle == 0)
        {
            Logger.Error($"BASS Error: {Bass.LastError} while creating track from file: {path}",
                new BassException(Bass.LastError));
            return;
        }

        Length = Bass.ChannelBytes2Seconds(decoderStreamHandle, Bass.ChannelGetLength(decoderStreamHandle)) * 1000.0;
    }

    public IAudioChannel GetChannel()
    {
        int channelHandle = 0;
        var flags = BassFlags.Decode | BassFlags.Float;

        if (filePath != null)
        {
            channelHandle = Bass.CreateStream(filePath, 0, 0, flags);
        }
        else if (dataPtr != System.IntPtr.Zero)
        {
            channelHandle = Bass.CreateStream(dataPtr, 0, dataLength, flags);
        }

        if (channelHandle == 0)
        {
            Logger.Error($"BASS Error: {Bass.LastError} while creating playback channel for track.",
                new BassException(Bass.LastError));
            return null;
        }

        var channel = manager.CreateChannel(channelHandle, true, manager.TrackMixer);

        // Set loop restart point if looping
        channel.Looping = true; // Tracks often loop

        if (RestartPoint > 0)
        {
            long restartPos = Bass.ChannelSeconds2Bytes(channelHandle, RestartPoint / 1000.0);
            Bass.ChannelSetSync(channelHandle, SyncFlags.End, 0, (handle, chan, data, user) =>
            {
                // When track ends, seek back to restart point
                Bass.ChannelSetPosition(chan, restartPos);
            });
        }

        return channel;
    }

    private bool isDisposed;

    public void Dispose()
    {
        Dispose(true);
#pragma warning disable CA1816
        GC.SuppressFinalize(this);
#pragma warning restore CA1816
    }

    protected virtual void Dispose(bool disposing)
    {
        if (isDisposed) return;

        if (decoderStreamHandle != 0)
        {
            Bass.StreamFree(decoderStreamHandle);
        }

        if (dataHandle.IsAllocated)
        {
            dataHandle.Free();
        }

        isDisposed = true;
    }

    ~BassTrack()
    {
        Dispose(false);
    }
}
