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
    private readonly IntPtr dataPtr;
    private readonly long dataLength;
    private GCHandle dataHandle;

    public double Length { get; }

    public BassSample(BassAudioManager manager, Stream stream)
    {
        this.manager = manager;

        using (var ms = new MemoryStream())
        {
            stream.CopyTo(ms);
            byte[] data = ms.ToArray();
            dataLength = data.Length;
            dataHandle = GCHandle.Alloc(data, GCHandleType.Pinned);
            dataPtr = dataHandle.AddrOfPinnedObject();
        }

        Length = calculateLength();
    }

    public BassSample(BassAudioManager manager, string path)
    {
        this.manager = manager;
        byte[] data = File.ReadAllBytes(path);
        dataLength = data.Length;
        dataHandle = GCHandle.Alloc(data, GCHandleType.Pinned);
        dataPtr = dataHandle.AddrOfPinnedObject();

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
        int channelHandle = Bass.CreateStream(dataPtr, 0, dataLength, BassFlags.Decode | BassFlags.Float);

        if (channelHandle == 0) return null;

        return manager.CreateChannel(channelHandle, true, manager.SampleMixer);
    }

    public IAudioChannel Play()
    {
        var channel = GetChannel();
        channel.AutoDispose = true;
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

        if (dataHandle.IsAllocated)
            dataHandle.Free();

        isDisposed = true;
    }

    ~BassSample()
    {
        Dispose(false);
    }
}
