// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using Sakura.Framework.Extensions.ObjectExtensions;
using Sakura.Framework.Logging;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// SDL implementation of <see cref="ISample"/>. Samples are decoded to PCM once at a load and shared
/// by every channel playing them.
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
internal sealed class SDLSample : ISample, IHasActiveChannels, IDisposable
{
    private readonly SDLAudioManager manager;
    private readonly PcmBuffer? buffer;

    private int activeChannelCount;
    private bool isDisposed;

    public double Length => buffer?.LengthMs ?? 0;

    public bool HasActiveChannels => Volatile.Read(ref activeChannelCount) > 0;

    public SDLSample(SDLAudioManager manager, Stream stream)
        : this(manager, () => stream)
    {
    }

    public SDLSample(SDLAudioManager manager, string path)
        : this(manager, () => File.OpenRead(path))
    {
    }

    private SDLSample(SDLAudioManager manager, Func<Stream> open)
    {
        this.manager = manager;

        try
        {
            buffer = PcmBuffer.Decode(open(), manager.SampleRate, manager.Channels);

            GlobalStatistics.Get<int>("Audio", "Loaded Samples").Value++;
            Logger.Verbose($"🔈 Sample decoded to PCM ({buffer.Samples.Length * sizeof(float) / 1024} KB, {buffer.LengthMs:F0}ms)");
        }
        catch (Exception e)
        {
            Logger.Error("Could not decode a sample.", e);
            buffer = null;
        }
    }

    public IAudioChannel GetChannel()
    {
        if (isDisposed || buffer == null)
            return null!;

        var source = new MemoryPcmSource(buffer);
        var channel = manager.CreateChannel(source, manager.SampleMixer, streaming: false);

        Interlocked.Increment(ref activeChannelCount);
        channel.Disposed += () => Interlocked.Decrement(ref activeChannelCount);

        return channel;
    }

    public IAudioChannel Play()
    {
        var channel = GetChannel();

        if (channel.IsNull())
            return null!;

        channel.AutoDispose = true;
        channel.Play();

        return channel;
    }

    public void Dispose()
    {
        if (isDisposed)
            return;

        isDisposed = true;

        if (buffer != null)
            GlobalStatistics.Get<int>("Audio", "Loaded Samples").Value--;

        // Channels hold their own reference to the buffer through MemoryPcmSource, and the GC frees
        // it once the last of them is gone — there is no unmanaged block to release here.
    }
}
