// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using System.Runtime.InteropServices;
using ManagedBass;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Audio.BassEngine;

internal class BassSample : ISample
{
    private readonly BassAudioManager manager;
    private readonly int sampleHandle;

    public double Length { get; }

    public BassSample(BassAudioManager manager, Stream stream)
    {
        this.manager = manager;

        using (var ms = new MemoryStream())
        {
            stream.CopyTo(ms);
            byte[] data = ms.ToArray();
            var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                sampleHandle = Bass.SampleLoad(handle.AddrOfPinnedObject(), 0, data.Length, 255, BassFlags.Default);
            }
            finally
            {
                if (handle.IsAllocated)
                    handle.Free();
            }
        }

        if (sampleHandle == 0)
        {
            Logger.Error($"BASS Error: {Bass.LastError} while creating sample from stream.", new BassException(Bass.LastError));
            return;
        }

        var info = Bass.SampleGetInfo(sampleHandle);
        Length = (double)info.Length / info.Frequency / info.Channels * 1000.0;
    }

    public BassSample(BassAudioManager manager, string path)
    {
        this.manager = manager;

        // Load sample data from file. Max 255 simultaneous plays.
        sampleHandle = Bass.SampleLoad(path, 0, 0, 255, BassFlags.Default);

        if (sampleHandle == 0)
        {
            Logger.Error($"BASS Error: {Bass.LastError} while creating sample from file: {path}", new BassException(Bass.LastError));
            return;
        }

        var info = Bass.SampleGetInfo(sampleHandle);
        Length = (double)info.Length / info.Frequency / info.Channels * 1000.0;
    }

    public IAudioChannel GetChannel()
    {
        int channelHandle = Bass.SampleGetChannel(sampleHandle);
        if (channelHandle == 0)
            return null;

        var channel = manager.CreateChannel(channelHandle, false);
        return channel;
    }

    public IAudioChannel Play()
    {
        var channel = GetChannel();
        channel.Play();
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

        if (sampleHandle != 0)
        {
            Bass.SampleFree(sampleHandle);
        }

        isDisposed = true;
    }

    ~BassSample()
    {
        Dispose(false);
    }
}
