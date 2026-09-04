// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using ManagedBass;
using ManagedBass.Fx;
using ManagedBass.Mix;
using Sakura.Framework.Logging;
using Sakura.Framework.Reactive;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Audio.BassEngine;

/// <summary>
/// BASS implementation of the IAudioManager.
/// Initializes BASS and creates BASS-backed tracks, samples, and channels.
/// </summary>
internal class BassAudioManager : IAudioManager, IDisposable
{
    private readonly List<BassAudioChannel> activeChannels = new List<BassAudioChannel>();
    private readonly ConcurrentQueue<Action> audioThreadActions = new ConcurrentQueue<Action>();
    private readonly SyncProcedure channelEndSync;

    private readonly BassAudioMixer trackMixer;
    private readonly BassAudioMixer sampleMixer;
    private bool ownsBassInit;

    public Reactive<double> MasterVolume { get; } = new Reactive<double>(1.0);
    public Reactive<double> TrackVolume { get; } = new Reactive<double>(1.0);
    public Reactive<double> SampleVolume { get; } = new Reactive<double>(1.0);

    /// <summary>
    /// Output latency in milliseconds, as BASS measured it at initialisation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The counterpart to <see cref="SdlEngine.SDLAudioManager.OutputLatencyMs"/>, published so the two
    /// backends can be compared on the number this framework's second audio backend exists to improve.
    /// Comparing them needs one caveat kept in mind, because they are not measuring quite the same
    /// thing.
    /// </para>
    /// <para>
    /// BASS keeps a playback buffer — 100 ms here, see <see cref="Configuration.PlaybackBufferLength"/>
    /// — but already accounts for it when reporting a channel's position, so it does not appear in this
    /// figure and does not desync anything. What this figure is, is the part below that: the delay
    /// between BASS handing audio to the device and the listener hearing it. The SDL backend's number
    /// is the same quantity arrived at differently — its stream queue plus its device buffer — and it
    /// is likewise subtracted from reported positions, by <c>OutputLatencyCompensator</c>.
    /// </para>
    /// <para>
    /// Zero means BASS would not say. It only measures this when asked at init, which is why
    /// <see cref="DeviceInitFlags.Latency"/> is passed below, and it cannot measure it at all when
    /// attaching to a device someone else already initialised.
    /// </para>
    /// </remarks>
    public double OutputLatencyMs { get; private set; }

    /// <summary>
    /// The playback buffer BASS is keeping, in milliseconds.
    /// </summary>
    /// <remarks>
    /// Not output latency — BASS compensates for this in its position reporting — but the closest
    /// BASS analogue to the SDL backend's device buffer setting, and the knob that would be turned to
    /// trade robustness for responsiveness here.
    /// </remarks>
    internal int PlaybackBufferMs { get; private set; }

    /// <param name="device">
    /// The BASS device index to open, or -1 for the system default. <see cref="Bass.NoSoundDevice"/>
    /// initialises BASS without an output device: channels still decode and their positions still
    /// advance in real time, but nothing is heard. That is what the cross-backend conformance suite
    /// runs on — the alternative is a test run that plays music out loud — and it is also the right
    /// mode for a headless host that only needs decoding.
    /// </param>
    public BassAudioManager(int device = -1)
    {
        // DeviceInitFlags.Latency asks BASS to measure the device's output latency during Init, which is
        // the only time it can be measured and the only way BassInfo.Latency is ever populated. It costs
        // a little startup time — BASS plays a short test buffer to time it — and buys the one figure
        // that makes this backend comparable with the SDL one.
        bool initSuccess = Bass.Init(device, 44100, DeviceInitFlags.Latency);
        bool alreadyInitialised = !initSuccess && Bass.LastError == Errors.Already;

        if (!initSuccess && !alreadyInitialised)
        {
            Logger.Error("BASS initialization failed!", new BassException(Bass.LastError));
        }
        else
        {
            ownsBassInit = initSuccess;

            if (alreadyInitialised)
                Logger.Verbose("🔈 BASS already initialised, reusing existing device");

            Bass.Configure(Configuration.UpdatePeriod, 5);
            Bass.Configure(Configuration.DeviceBufferLength, -1);
            Bass.Configure(Configuration.PlaybackBufferLength, 100);

            trackMixer = new BassAudioMixer(this);
            sampleMixer = new BassAudioMixer(this);

            trackMixer.Play();
            sampleMixer.Play();

            TrackVolume.ValueChanged += e => trackMixer.Volume.Value = e.NewValue;
            SampleVolume.ValueChanged += e => sampleMixer.Volume.Value = e.NewValue;
            MasterVolume.ValueChanged += e =>
            {
                trackMixer.Volume.Value = TrackVolume.Value * e.NewValue;
                sampleMixer.Volume.Value = SampleVolume.Value * e.NewValue;
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

            PlaybackBufferMs = playbackBuffer;

            if (Bass.GetInfo(out var info))
            {
                OutputLatencyMs = info.Latency;

                // Said the same way round as the SDL backend's line, so a bug report from either one can
                // be read against the other without converting anything.
                Logger.Verbose(info.Latency > 0
                    ? $"Output latency: {info.Latency} ms (device), plus a {playbackBuffer} ms playback buffer BASS compensates for"
                    : $"Output latency: not measured by BASS{(alreadyInitialised ? " (attached to a device someone else initialised)" : string.Empty)}");

                Logger.Verbose($"Minimum buffer: {info.MinBufferLength} ms");
            }
        }
        channelEndSync = OnChannelEnded;
    }

    public IAudioMixer TrackMixer => trackMixer;
    public IAudioMixer SampleMixer => sampleMixer;

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
    internal BassAudioChannel CreateChannel(int channelHandle, bool isStream, BassAudioMixer targetMixer = null)
    {
        if (isStream)
        {
            int tempoHandle = BassFx.TempoCreate(channelHandle, BassFlags.Decode | BassFlags.FxFreeSource);

            if (tempoHandle != 0)
            {
                channelHandle = tempoHandle;
            }
            else
            {
                Logger.Error($"BASS Error: {Bass.LastError} while creating tempo stream; falling back to source channel.", new BassException(Bass.LastError));
            }
        }

        var channel = new BassAudioChannel(channelHandle, this, isStream, targetMixer);

        lock (activeChannels)
        {
            activeChannels.Add(channel);
        }

        Bass.ChannelGetAttribute(channelHandle, ChannelAttribute.Frequency, out float _);
        targetMixer?.AddChannel(channel);

        Bass.ChannelSetSync(channelHandle, SyncFlags.End, 0, channelEndSync, IntPtr.Zero);

        return channel;
    }

    internal void RemoveChannel(BassAudioChannel channel)
    {
        lock (activeChannels)
        {
            activeChannels.Remove(channel);
        }
    }

    private void OnChannelEnded(int handle, int channel, int data, IntPtr user)
    {
        EnqueueAction(() =>
        {
            lock (activeChannels)
            {
                // Find the channel and clean it up
                for (int i = activeChannels.Count - 1; i >= 0; i--)
                {
                    var bassChannel = activeChannels[i];
                    if (bassChannel.ChannelHandle == handle)
                    {
                        bassChannel.IsRunning.Value = false;
                        if (bassChannel.AutoDispose)
                        {
                            bassChannel.Dispose();
                            activeChannels.RemoveAt(i);
                        }
                        break;
                    }
                }
            }
        });
    }

    /// <summary>
    /// Enqueues an action to be executed safely on audio thread
    /// </summary>
    public void EnqueueAction(Action action)
    {
        if (action != null)
        {
            audioThreadActions.Enqueue(action);
        }
    }

    public void Update(double frameTime)
    {
        while (audioThreadActions.TryDequeue(out var action))
        {
            action.Invoke();
        }

        GlobalStatistics.Get<double>("Audio", "BASS CPU Usage (%)").Value = Bass.CPUUsage;

        // Named to line up with "SDL Output Latency (ms)" so the two backends can be read against each
        // other in the same overlay. Constant for the life of the device, published every frame anyway
        // because a statistic that only appears once is a statistic nobody sees.
        GlobalStatistics.Get<double>("Audio", "BASS Output Latency (ms)").Value = OutputLatencyMs;
        GlobalStatistics.Get<int>("Audio", "BASS Playback Buffer (ms)").Value = PlaybackBufferMs;
    }

    public void Dispose()
    {
        BassAudioChannel[] channelsToDispose;
        lock (activeChannels)
        {
            channelsToDispose = activeChannels.ToArray();
        }
        foreach (var channel in channelsToDispose)
        {
            channel.Dispose();
        }
        activeChannels.Clear();

        if (ownsBassInit)
            Bass.Free();
    }

    public void StopAll()
    {
        BassAudioChannel[] channelsToDispose;

        lock (activeChannels)
        {
            channelsToDispose = activeChannels.ToArray();
            activeChannels.Clear();
        }

        foreach (var channel in channelsToDispose)
        {
            channel.Dispose();
        }
    }
}
