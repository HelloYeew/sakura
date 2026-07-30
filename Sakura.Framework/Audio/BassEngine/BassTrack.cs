// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using ManagedBass;
using Sakura.Framework.IO;
using Sakura.Framework.Logging;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Audio.BassEngine;

/// <summary>
/// BASS implementation of <see cref="ITrack"/>
/// </summary>
internal class BassTrack : ITrack, IHasActiveChannels, IDisposable
{
    private readonly BassAudioManager manager;

    private readonly string? filePath;

    /// <summary>
    /// The encoded file held in unmanaged memory, for tracks created from a stream. Null for
    /// file-backed tracks, where BASS reads the file itself and no copy is made at all.
    /// </summary>
    /// <remarks>
    /// BASS memory streams do not copy the data they are given (see <c>BASS_StreamCreateFile</c>),
    /// so the block has to stay valid for as long as the decoder stream and every playback channel
    /// built from it. It used to be a managed <c>byte[]</c> pinned with a <c>GCHandle</c> for the
    /// track's lifetime, which — with a store keeping ten tracks resident — left ten immovable
    /// multi-megabyte blocks the GC could never compact around. <see cref="NativeMemoryBuffer"/>
    /// keeps it off the managed heap entirely and reference counts it, so eviction can dispose this
    /// track while a channel is still reading.
    /// </remarks>
    private readonly NativeMemoryBuffer? data;

    private readonly IntPtr dataPtr;
    private readonly long dataLength;

    private readonly int decoderStreamHandle;

    private int activeChannelCount;

    public double Length { get; }
    public double RestartPoint { get; set; }

    public bool HasActiveChannels => Volatile.Read(ref activeChannelCount) > 0;

    /// <summary>
    /// Creates a track from a stream.
    /// </summary>
    public BassTrack(BassAudioManager manager, Stream stream)
    {
        this.manager = manager;
        filePath = null;

        data = NativeMemoryBuffer.CreateFrom(stream);

        if (data == null)
        {
            Logger.Error("Refusing to create a track from an empty stream.", new InvalidDataException("Audio stream contained no data."));
            return;
        }

        dataPtr = data.Pointer;
        dataLength = data.Length;

        BassAudioStatistics.AddNativeBufferBytes(dataLength);

        decoderStreamHandle = Bass.CreateStream(dataPtr, 0, dataLength, BassFlags.Decode | BassFlags.Prescan);

        if (decoderStreamHandle == 0)
        {
            Logger.Error($"BASS Error: {Bass.LastError} while creating track from stream.",
                new BassException(Bass.LastError));
            return;
        }

        GlobalStatistics.Get<int>("Audio", "Loaded Tracks").Value++;

        Length = Bass.ChannelBytes2Seconds(decoderStreamHandle, Bass.ChannelGetLength(decoderStreamHandle)) * 1000.0;

        Logger.Verbose($"🔈 Track loaded from stream ({dataLength / 1024} KB held in unmanaged memory)");
    }

    /// <summary>
    /// Creates a track from a file path. Preferred over the stream constructor whenever a real file
    /// is available: BASS reads the file on demand, so the encoded audio never occupies managed or
    /// unmanaged memory of ours at all.
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

        GlobalStatistics.Get<int>("Audio", "Loaded Tracks").Value++;

        Length = Bass.ChannelBytes2Seconds(decoderStreamHandle, Bass.ChannelGetLength(decoderStreamHandle)) * 1000.0;

        // Worth reporting which of the two routes a track took: the file route holds no copy of the
        // encoded audio, the stream route holds one for the track's lifetime.
        Logger.Verbose($"🔈 Track loaded from file, no in-memory copy: {path}");
    }

    public IAudioChannel GetChannel()
    {
        int channelHandle = 0;
        var flags = BassFlags.Decode | BassFlags.Float;

        // A reference on the data block, held for as long as the channel exists. Without it, an
        // eviction (or any other Dispose) between here and the channel's own disposal would free
        // the memory BASS is still decoding from.
        bool holdsDataReference = false;

        if (filePath != null)
        {
            channelHandle = Bass.CreateStream(filePath, 0, 0, flags);
        }
        else if (data != null)
        {
            holdsDataReference = data.AddReference();

            if (holdsDataReference)
                channelHandle = Bass.CreateStream(dataPtr, 0, dataLength, flags);
        }

        if (channelHandle == 0)
        {
            if (holdsDataReference)
                releaseDataReference();

            Logger.Error($"BASS Error: {Bass.LastError} while creating playback channel for track.",
                new BassException(Bass.LastError));
            return null!;
        }

        var channel = manager.CreateChannel(channelHandle, true, (BassAudioMixer)manager.TrackMixer);

        Interlocked.Increment(ref activeChannelCount);

        // Runs after the channel's own BASS handles have been freed, which is the only point at
        // which the data block is guaranteed to be out of use by this channel.
        channel.Disposed += () =>
        {
            Interlocked.Decrement(ref activeChannelCount);

            if (holdsDataReference)
                releaseDataReference();
        };

        // Set loop restart point if looping
        channel.Looping = true; // Tracks often loop

        if (RestartPoint > 0)
        {
            long restartPos = Bass.ChannelSeconds2Bytes(channelHandle, RestartPoint / 1000.0);

            SyncProcedure restartSync = (handle, chan, syncData, user) =>
            {
                Bass.ChannelSetPosition(chan, restartPos);
            };

            lock (syncProcedures)
                syncProcedures.Add(restartSync);

            Bass.ChannelSetSync(channelHandle, SyncFlags.End, 0, restartSync);
        }

        return channel;
    }

    /// <summary>
    /// Sync callbacks handed to BASS, held to keep them from being collected while a channel can still
    /// invoke them. Cleared on <see cref="Dispose(bool)"/>, once every channel built from this track's
    /// data is gone.
    /// </summary>
    private readonly List<SyncProcedure> syncProcedures = new List<SyncProcedure>();

    private void releaseDataReference()
    {
        if (data == null)
            return;

        if (data.Release())
            BassAudioStatistics.AddNativeBufferBytes(-dataLength);
    }

    private bool isDisposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (isDisposed) return;

        isDisposed = true;

        if (decoderStreamHandle != 0)
        {
            Bass.StreamFree(decoderStreamHandle);
            GlobalStatistics.Get<int>("Audio", "Loaded Tracks").Value--;
        }

        if (disposing)
        {
            // Only the reference this track owns. Channels still playing hold their own, and the
            // block survives until the last of them is disposed.
            releaseDataReference();

            lock (syncProcedures)
                syncProcedures.Clear();
        }

        // On the finalizer path the buffer is left to its own finalizer: it is a managed object and
        // may already have been collected, and it can only be finalized once nothing — including
        // any channel — can still read from it.
    }

    ~BassTrack()
    {
        Dispose(false);
    }
}
