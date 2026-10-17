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

    /// <summary>
    /// The decoded PCM, for the managed mixer. Null on the native path, where the engine holds its own
    /// copy and this one would only be a second copy of the same audio.
    /// </summary>
    private readonly PcmBuffer? buffer;

    /// <summary>
    /// The native engine's copy of the decoded PCM, shared by every voice playing this sample, or 0 on
    /// the managed path.
    /// </summary>
    private uint nativeBuffer;

    private int activeChannelCount;
    private bool isDisposed;

    public double Length { get; }

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
            long decodedBytes;

            if (manager.UsesNativeMixEngine)
            {
                // Decoded straight into unmanaged memory and copied into the engine's pool: the audio
                // thread reads it without touching anything managed, and no managed array of it is ever
                // made, so a beatmap's worth of hitsounds no longer lands on the large object heap.
                nativeBuffer = PcmBuffer.DecodeIntoEngine(open(), manager.NativeEngine!, out double lengthMs);

                if (nativeBuffer == 0)
                    throw new InvalidOperationException("The native mix engine would not take the decoded sample.");

                Length = lengthMs;
                decodedBytes = (long)(lengthMs / 1000.0 * manager.SampleRate) * manager.Channels * sizeof(float);
            }
            else
            {
                buffer = PcmBuffer.Decode(open(), manager.SampleRate, manager.Channels);
                Length = buffer.LengthMs;
                decodedBytes = (long)buffer.Samples.Length * sizeof(float);
            }

            GlobalStatistics.Get<int>("Audio", "Loaded Samples", StatisticKind.Cumulative).Value++;
            Logger.Verbose($"🔈 Sample decoded to PCM ({decodedBytes / 1024} KB, {Length:F0}ms)");
        }
        catch (Exception e)
        {
            Logger.Error("Could not decode a sample.", e);
            buffer = null;
            Length = 0;
        }
    }

    public IAudioChannel GetChannel()
    {
        if (isDisposed)
            return null!;

        ISDLChannel? channel;

        if (manager.UsesNativeMixEngine)
        {
            if (nativeBuffer == 0)
                return null!;

            channel = manager.CreateNativeBufferChannel(nativeBuffer, Length, manager.SampleMixer);
        }
        else
        {
            if (buffer == null)
                return null!;

            channel = manager.CreateChannel(new MemoryPcmSource(buffer), manager.SampleMixer, streaming: false);
        }

        if (channel == null)
            return null!;

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

        if (buffer != null || nativeBuffer != 0)
            GlobalStatistics.Get<int>("Audio", "Loaded Samples", StatisticKind.Cumulative).Value--;

        // On the managed path, channels hold their own reference to the buffer through
        // MemoryPcmSource and the GC frees it once the last of them is gone. On the native path this
        // drops only *this* object's claim on the engine's copy; every playing voice holds one of its
        // own, so a sample disposed mid-hitsound does not cut it off.
        if (nativeBuffer != 0)
        {
            manager.NativeEngine?.ReleaseBuffer(nativeBuffer);
            nativeBuffer = 0;
        }
    }
}
