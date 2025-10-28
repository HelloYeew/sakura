// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

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

    // FIX 2: Store the source of the track data
    private readonly string filePath;
    private readonly System.IntPtr dataPtr;
    private readonly long dataLength;
    private GCHandle dataHandle;

    private readonly int decoderStreamHandle; // This is the handle to the *decoder*

    public double Length { get; }
    public double RestartPoint { get; set; }

    /// <summary>
    /// Creates a track from a stream.
    /// </summary>
    public BassTrack(BassAudioManager manager, Stream stream)
    {
        this.manager = manager;
        this.filePath = null; // Mark as stream-based

        using (var ms = new MemoryStream())
        {
            stream.CopyTo(ms);
            var data = ms.ToArray();
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
        this.filePath = path; // Mark as file-based
        this.dataPtr = System.IntPtr.Zero;

        decoderStreamHandle = Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Prescan);

        if (decoderStreamHandle == 0)
        {
            Logger.Error($"BASS Error: {Bass.LastError} while creating track from file: {path}",
                new BassException(Bass.LastError));
            return;
        }

        Length = Bass.ChannelBytes2Seconds(decoderStreamHandle, Bass.ChannelGetLength(decoderStreamHandle)) * 1000.0;
    }

    public IAudioChannel Play()
    {
        // FIX 2: Create a new *playback stream* from the original data source, not from the decoder.
        int channelHandle = 0;
        if (filePath != null)
        {
            channelHandle = Bass.CreateStream(filePath, 0, 0, BassFlags.Default);
        }
        else if (dataPtr != System.IntPtr.Zero)
        {
            channelHandle = Bass.CreateStream(dataPtr, 0, dataLength, BassFlags.Default);
        }

        if (channelHandle == 0)
        {
            Logger.Error($"BASS Error: {Bass.LastError} while creating playback channel for track.",
                new BassException(Bass.LastError));
            return null;
        }

        var channel = manager.CreateChannel(channelHandle, true);

        // Set loop restart point if looping
        channel.Looping = true; // Tracks often loop

        // FIX 3: Use ChannelSetPosition with PositionFlags.Loop to set the loop start point.
        if (RestartPoint > 0)
        {
            long restartPos = Bass.ChannelSeconds2Bytes(channelHandle, RestartPoint / 1000.0);
            Bass.ChannelSetSync(channelHandle, SyncFlags.End, 0, (handle, chan, data, user) =>
            {
                // When track ends, seek back to restart point
                Bass.ChannelSetPosition(chan, restartPos);
            });
        }

        channel.Play();
        return channel;
    }

    ~BassTrack()
    {
        Dispose(false);
    }

    public void Dispose(bool disposing)
    {
        // Free the decoder stream
        if (decoderStreamHandle != 0)
        {
            Bass.StreamFree(decoderStreamHandle);
        }

        // Free the unmanaged memory handle
        if (dataHandle.IsAllocated)
        {
            dataHandle.Free();
        }
    }
}
