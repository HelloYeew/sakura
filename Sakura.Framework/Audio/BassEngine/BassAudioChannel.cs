// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using ManagedBass;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Audio.BassEngine;

/// <summary>
/// BASS implementation of <see cref="IAudioChannel"/>.
/// </summary>
internal class BassAudioChannel : IAudioChannel
{
    public event Action OnStart;
    public event Action OnStop;
    public event Action OnEnd;

    public ReactiveBool IsRunning { get; } = new ReactiveBool();

    public int ChannelHandle { get; }

    private readonly BassAudioManager manager;
    private readonly bool isStream;
    private SyncProcedure endSyncProcedure; // Keep a reference to prevent GC
    private bool isLooping;

    public BassAudioChannel(int channelHandle, BassAudioManager manager, bool isStream)
    {
        ChannelHandle = channelHandle;
        this.manager = manager;
        this.isStream = isStream;

        // Set up a sync to fire the OnEnd event
        endSyncProcedure = new SyncProcedure(OnChannelEnd);
        Bass.ChannelSetSync(ChannelHandle, SyncFlags.End, 0, endSyncProcedure);

        // Set up reactive property bindings
        IsRunning.ValueChanged += e =>
        {
            if (e.NewValue) OnStart?.Invoke();
            else OnStop?.Invoke();
        };

        Volume.ValueChanged += e => BassUtils.CheckError(Bass.ChannelSetAttribute(ChannelHandle, ChannelAttribute.Volume, (float)e.NewValue), "setting volume");
        Frequency.ValueChanged += e => BassUtils.CheckError(Bass.ChannelSetAttribute(ChannelHandle, ChannelAttribute.Frequency, (float)(e.NewValue * manager.GetOriginalFrequency(ChannelHandle))), "setting frequency");
        Balance.ValueChanged += e => BassUtils.CheckError(Bass.ChannelSetAttribute(ChannelHandle, ChannelAttribute.Pan, (float)e.NewValue), "setting balance");
    }

    private void OnChannelEnd(int handle, int channel, int data, IntPtr user)
    {
        // This callback is from a BASS thread.
        // We schedule the event to run on the main audio thread (via manager update).
        manager.ScheduleMainThreadAction(() =>
        {
            OnEnd?.Invoke();
            if (!isLooping)
            {
                IsRunning.Value = false;
            }
        });
    }

    public void Play()
    {
        if (BassUtils.CheckError(Bass.ChannelPlay(ChannelHandle, false), "playing channel"))
        {
            IsRunning.Value = true;
        }
    }

    public void Stop()
    {
        if (BassUtils.CheckError(Bass.ChannelStop(ChannelHandle), "stopping channel"))
        {
            // Reset position to start
            Bass.ChannelSetPosition(ChannelHandle, 0);
            IsRunning.Value = false;
        }
    }

    public void Pause()
    {
        if (BassUtils.CheckError(Bass.ChannelPause(ChannelHandle), "pausing channel"))
        {
            IsRunning.Value = false;
        }
    }

    public Reactive<double> Volume { get; } = new Reactive<double>(1.0);
    public Reactive<double> Frequency { get; } = new Reactive<double>(1.0);
    public Reactive<double> Balance { get; } = new Reactive<double>(0.0);

    public double CurrentTime
    {
        get
        {
            long pos = Bass.ChannelGetPosition(ChannelHandle);
            return Bass.ChannelBytes2Seconds(ChannelHandle, pos) * 1000.0;
        }
        set
        {
            long pos = Bass.ChannelSeconds2Bytes(ChannelHandle, value / 1000.0);
            Bass.ChannelSetPosition(ChannelHandle, pos);
        }
    }

    public double Length
    {
        get
        {
            long len = Bass.ChannelGetLength(ChannelHandle);
            return Bass.ChannelBytes2Seconds(ChannelHandle, len) * 1000.0;
        }
    }

    public bool Looping
    {
        get => isLooping;
        set
        {
            isLooping = value;
            if (isLooping)
                Bass.ChannelFlags(ChannelHandle, BassFlags.Loop, BassFlags.Loop);
            else
                Bass.ChannelFlags(ChannelHandle, BassFlags.Default, BassFlags.Loop);
        }
    }

    public void Dispose()
    {
        // If it's a stream, we free the stream.
        // If it's a sample channel, BASS manages it, and it's freed when the sample is freed.
        if (isStream)
        {
            Bass.StreamFree(ChannelHandle);
        }
        IsRunning.Value = false;
        OnStart = null;
        OnStop = null;
        OnEnd = null;
    }
}
