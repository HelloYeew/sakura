// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.IO;
using System.Threading;
using ManagedBass;
using Sakura.Framework.IO;
using Sakura.Framework.Logging;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Audio.BassEngine;

internal class BassSample : ISample, IHasActiveChannels, IDisposable
{
    private readonly BassAudioManager manager;

    /// <summary>
    /// The encoded sample held in unmanaged memory. Every playback channel decodes straight out of
    /// this block, so it must stay at a fixed address for as long as any of them live — see
    /// <see cref="BassTrack"/> for why that is not a pinned managed array.
    /// </summary>
    private readonly NativeMemoryBuffer data;

    private readonly IntPtr dataPtr;
    private readonly long dataLength;

    private int activeChannelCount;

    public double Length { get; }

    public bool HasActiveChannels => Volatile.Read(ref activeChannelCount) > 0;

    public BassSample(BassAudioManager manager, Stream stream)
        : this(manager, NativeMemoryBuffer.CreateFrom(stream))
    {
    }

    public BassSample(BassAudioManager manager, string path)
        : this(manager, NativeMemoryBuffer.CreateFromFile(path))
    {
    }

    private BassSample(BassAudioManager manager, NativeMemoryBuffer data)
    {
        this.manager = manager;
        this.data = data;

        if (data == null)
        {
            Logger.Error("Refusing to create a sample with no data.", new InvalidDataException("Sample source contained no data."));
            return;
        }

        dataPtr = data.Pointer;
        dataLength = data.Length;

        BassAudioStatistics.AddNativeBufferBytes(dataLength);

        GlobalStatistics.Get<int>("Audio", "Loaded Samples").Value++;

        Length = calculateLength();
    }

    private double calculateLength()
    {
        int tempStream = Bass.CreateStream(dataPtr, 0, dataLength, BassFlags.Decode);
        if (tempStream != 0)
        {
            double length = Bass.ChannelBytes2Seconds(tempStream, Bass.ChannelGetLength(tempStream)) * 1000.0;
            Bass.StreamFree(tempStream);
            return length;
        }

        Logger.Error($"BASS Error: {Bass.LastError} while loading sample.", new BassException(Bass.LastError));
        return 0;
    }

    public IAudioChannel GetChannel()
    {
        if (data == null || !data.AddReference())
            return null!;

        int channelHandle = Bass.CreateStream(dataPtr, 0, dataLength, BassFlags.Decode | BassFlags.Float);

        if (channelHandle == 0)
        {
            releaseDataReference();
            return null!;
        }

        var channel = manager.CreateChannel(channelHandle, true, (BassAudioMixer)manager.SampleMixer);

        Interlocked.Increment(ref activeChannelCount);

        // Raised once BASS has freed the channel's own handles, so the data block is no longer being
        // read by it. Samples are fire-and-forget (see Play), so this is the normal path.
        channel.Disposed += () =>
        {
            Interlocked.Decrement(ref activeChannelCount);
            releaseDataReference();
        };

        return channel;
    }

    public IAudioChannel Play()
    {
        var channel = GetChannel();

        if (channel == null)
            return null!;

        channel.AutoDispose = true;
        channel.Play();
        return channel;
    }

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
#pragma warning disable CA1816
        GC.SuppressFinalize(this);
#pragma warning restore CA1816
    }

    protected virtual void Dispose(bool disposing)
    {
        if (isDisposed) return;

        isDisposed = true;

        if (data != null)
            GlobalStatistics.Get<int>("Audio", "Loaded Samples").Value--;

        // Only this sample's own reference. Channels still playing hold theirs, and the block
        // survives until the last one is disposed. On the finalizer path the buffer is left to its
        // own finalizer — it is a managed object and may already have been collected.
        if (disposing)
            releaseDataReference();
    }

    ~BassSample()
    {
        Dispose(false);
    }
}
