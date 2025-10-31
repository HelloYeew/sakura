// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.IO;
using System.Runtime.InteropServices;
using ManagedBass;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Audio.BassEngine;

internal class BassSample : ISample
{
    private readonly BassAudioManager manager;
    private readonly int sampleHandle;

    private GCHandle dataHandle;
    private System.IntPtr dataPtr;

    public double Length { get; }

    public BassSample(BassAudioManager manager, Stream stream)
    {
        this.manager = manager;

        using (var ms = new MemoryStream())
        {
            stream.CopyTo(ms);
            var data = ms.ToArray();
            dataHandle = GCHandle.Alloc(data, GCHandleType.Pinned);
            dataPtr = dataHandle.AddrOfPinnedObject();

            // Load sample data. Max 255 simultaneous plays.
            sampleHandle = Bass.SampleLoad(dataPtr, 0, data.Length, 255, BassFlags.Default);
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

    public IAudioChannel Play()
    {
        int channelHandle = Bass.SampleGetChannel(sampleHandle);
        if (channelHandle == 0)
            return null;

        var channel = manager.CreateChannel(channelHandle, false);
        channel.Play();
        return channel;
    }

    ~BassSample()
    {
        Dispose(false);
    }

    public void Dispose(bool disposing)
    {
        if (sampleHandle != 0)
        {
            Bass.SampleFree(sampleHandle);
        }
        if (dataHandle.IsAllocated)
        {
            dataHandle.Free();
        }
    }
}
