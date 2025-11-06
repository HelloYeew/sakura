// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using ManagedBass;
using ManagedBass.Fx;
using ManagedBass.Mix;
using Sakura.Framework.Logging;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Audio.BassEngine;

/// <summary>
/// BASS implementation of the IAudioManager.
/// Initializes BASS and creates BASS-backed tracks, samples, and channels.
/// </summary>
public class BassAudioManager : IAudioManager, IDisposable
{
    private readonly List<BassAudioChannel> activeChannels = new List<BassAudioChannel>();
    private readonly ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();
    private readonly ConcurrentDictionary<int, float> originalFrequencies = new ConcurrentDictionary<int, float>();

    public Reactive<double> MasterVolume { get; } = new Reactive<double>(1.0);
    public Reactive<double> TrackVolume { get; } = new Reactive<double>(1.0);
    public Reactive<double> SampleVolume { get; } = new Reactive<double>(1.0);

    public BassAudioManager()
    {
        if (!Bass.Init(-1, 44100, DeviceInitFlags.Default))
        {
            Logger.Error("BASS initialization failed!", new BassException(Bass.LastError));
        }
        else
        {
            Bass.Configure(Configuration.UpdatePeriod, 5);
            Bass.Configure(Configuration.DeviceBufferLength, -1);
            Bass.Configure(Configuration.PlaybackBufferLength, 100);

            TrackVolume.ValueChanged += e => Bass.GlobalStreamVolume = (int)e.NewValue * 10000; // From 0 to 10,000
            SampleVolume.ValueChanged += e => Bass.GlobalSampleVolume = (int)e.NewValue * 10000; // From 0 to 10,000
            MasterVolume.ValueChanged += e =>
            {
                Bass.GlobalStreamVolume = (int)(TrackVolume.Value * e.NewValue * 10000);
                Bass.GlobalSampleVolume = (int)(SampleVolume.Value * e.NewValue * 10000);
            };

            Logger.Verbose("🔈 BASS initialised");

            var version = Bass.Version;
            Logger.Verbose($"BASS version: {version.Major}.{version.Minor}.{version.Build}.{version.Revision}");

            try
            {
                var fxVersion = BassFx.Version;
                Logger.Verbose($"BASS FX version: {fxVersion.Major}.{fxVersion.Minor}.{fxVersion.Build}.{fxVersion.Revision}");
            }
            catch (DllNotFoundException)
            {
                Logger.Verbose("BASS FX version: Not loaded");
            }

            try
            {
                var mixVersion = BassMix.Version;
                Logger.Verbose($"BASS MIX version: {mixVersion.Major}.{mixVersion.Minor}.{mixVersion.Build}.{mixVersion.Revision}");
            }
            catch (DllNotFoundException)
            {
                Logger.Verbose("BASS MIX version: Not loaded");
            }

            if (Bass.GetDeviceInfo(Bass.CurrentDevice, out var deviceInfo))
            {
                Logger.Verbose($"Device: {deviceInfo.Name}");
                Logger.Verbose($"Driver: {deviceInfo.Driver}");
            }

            int updatePeriod = Bass.GetConfig(Configuration.UpdatePeriod);
            int deviceBuffer = Bass.GetConfig(Configuration.DeviceBufferLength);
            int playbackBuffer = Bass.GetConfig(Configuration.PlaybackBufferLength);

            Logger.Verbose($"Update period: {updatePeriod} ms");
            Logger.Verbose($"Device buffer length: {deviceBuffer} ms");
            Logger.Verbose($"Playback buffer length: {playbackBuffer} ms");
        }
    }

    public ITrack CreateTrack(Stream stream)
    {
        return new BassTrack(this, stream);
    }

    public ISample CreateSample(Stream stream)
    {
        return new BassSample(this, stream);
    }

    public ITrack CreateTrackFromFile(string path)
    {
        return new BassTrack(this, path);
    }

    public ISample CreateSampleFromFile(string path)
    {
        return new BassSample(this, path);
    }

    /// <summary>
    /// Creates a BASS channel wrapper and registers it.
    /// </summary>
    internal BassAudioChannel CreateChannel(int channelHandle, bool isStream)
    {
        var channel = new BassAudioChannel(channelHandle, this, isStream);
        activeChannels.Add(channel);

        // Store original frequency for pitch shifting
        float freq = 0;
        Bass.ChannelGetAttribute(channelHandle, ChannelAttribute.Frequency, out freq);
        originalFrequencies.TryAdd(channelHandle, freq);

        return channel;
    }

    internal float GetOriginalFrequency(int channelHandle)
    {
        return originalFrequencies.TryGetValue(channelHandle, out float freq) ? freq : 48000;
    }

    /// <summary>
    /// Schedules an action to be run on the main audio update thread.
    /// </summary>
    internal void ScheduleMainThreadAction(Action action)
    {
        if (action != null)
        {
            mainThreadActions.Enqueue(action);
        }
    }

    public void Update(double frameTime)
    {
        // Run actions scheduled from other threads (e.g., BASS SYNCPROC)
        while (mainThreadActions.TryDequeue(out var action))
        {
            action.Invoke();
        }

        // Clean up disposed or stopped channels
        for (int i = activeChannels.Count - 1; i >= 0; i--)
        {
            var channel = activeChannels[i];
            if (Bass.ChannelIsActive(channel.ChannelHandle) == PlaybackState.Stopped)
            {
                if (channel.IsRunning.Value)
                {
                    // Wasn't stopped manually, so it must have ended
                    channel.IsRunning.Value = false;
                }
            }

            // TODO: Remve disposed channels or sample can be removed if needed
        }
        Bass.Update((int)Math.Max(1, frameTime));
    }

    public void Dispose()
    {
        // Free all active channels
        foreach (var channel in activeChannels)
        {
            channel.Dispose();
        }
        activeChannels.Clear();
        originalFrequencies.Clear();

        // Free BASS
        Bass.Free();
    }
}
