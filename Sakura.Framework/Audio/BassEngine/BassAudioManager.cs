// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ManagedBass;
using ManagedBass.Fx;
using ManagedBass.Mix;
using Sakura.Framework.Logging;
using Sakura.Framework.Reactive;
using Sakura.Framework.Statistic;
using Sakura.Framework.Timing;

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

    public Reactive<double> MasterVolume { get; } = new Reactive<double>(1.0);
    public Reactive<double> TrackVolume { get; } = new Reactive<double>(1.0);
    public Reactive<double> SampleVolume { get; } = new Reactive<double>(1.0);

    public IReadOnlyList<string> OutputDevices => outputDevices;

    public event Action OutputDevicesChanged;

    public Reactive<string> OutputDevice { get; } = new Reactive<string>(string.Empty);

    // How often the device list is read again, for devices plugged in and out.
    private const double device_check_interval_ms = 1000;

    // BASS's output devices by name, and the index each is opened by. Read by the update thread, so replaced whole.
    private volatile string[] outputDevices = Array.Empty<string>();
    private int[] outputDeviceIndices = Array.Empty<int>();

    // BASS's "Default" device, which follows the system's default output as it changes, or 0 where there's none.
    private int followingDefaultDevice;

    // The device the mixers play on, and every device this manager initialised, to free at the end.
    private int playingDevice;
    private readonly List<int> initialisedDevices = new List<int>();

    private double sinceDeviceCheckMs;

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
    /// <param name="outputDevice">
    /// The device to play on, by name, where <paramref name="device"/> is the default: the starting
    /// <see cref="OutputDevice"/>. Empty, or a device that isn't there, leaves the system's default.
    /// </param>
    public BassAudioManager(int device = -1, string outputDevice = "")
    {
        // A "Default" device that follows the system's default output, as the empty OutputDevice does with SDL. macOS
        // lists one regardless; Windows only when asked, before BASS starts.
        Bass.IncludeDefaultDevice = true;

        refreshOutputDevices();

        OutputDevice.Value = outputDevice;

        if (device == -1)
            device = indexOf(outputDevice) ?? (followingDefaultDevice > 0 ? followingDefaultDevice : -1);

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
            playingDevice = Bass.CurrentDevice;

            if (initSuccess)
                initialisedDevices.Add(playingDevice);

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

            // Not for the silent device: it's chosen to play nothing, and stays so.
            if (device != Bass.NoSoundDevice)
                OutputDevice.ValueChanged += _ => EnqueueAction(followPreferredDevice);

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

    /// <summary>
    /// Reads BASS's output devices.
    /// </summary>
    /// <returns>Whether they changed since the last read.</returns>
    private bool refreshOutputDevices()
    {
        var names = new List<string>();
        var indices = new List<int>();

        followingDefaultDevice = 0;

        // 0 is BASS's "no sound" device, not one to play on.
        for (int i = 1; Bass.GetDeviceInfo(i, out var info); i++)
        {
            if (!info.IsEnabled)
                continue;

            // The "Default" device is the only one with no driver behind it. It's what the empty name plays on, so it
            // isn't listed beside the devices it stands for.
            if (string.IsNullOrEmpty(info.Driver))
            {
                followingDefaultDevice = i;
                continue;
            }

            names.Add(info.Name);
            indices.Add(i);
        }

        if (indices.AsReadOnly().SequenceEqual(outputDeviceIndices) && names.AsReadOnly().SequenceEqual(outputDevices))
            return false;

        outputDeviceIndices = indices.ToArray();
        outputDevices = names.ToArray();
        return true;
    }

    private int? indexOf(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        int position = Array.IndexOf(outputDevices, name);
        return position >= 0 ? outputDeviceIndices[position] : null;
    }

    private int defaultDevice()
    {
        if (followingDefaultDevice > 0)
            return followingDefaultDevice;

        for (int i = 0; i < outputDeviceIndices.Length; i++)
        {
            if (Bass.GetDeviceInfo(outputDeviceIndices[i], out var info) && info.IsDefault)
                return outputDeviceIndices[i];
        }

        return playingDevice;
    }

    /// <summary>
    /// Moves the mixers onto the device the player picked, or onto the default while it isn't there. Everything plays
    /// through the two mixers, so moving them moves all of it.
    /// </summary>
    private void followPreferredDevice()
    {
        if (trackMixer == null || playingDevice == Bass.NoSoundDevice)
            return;

        int target = indexOf(OutputDevice.Value) ?? defaultDevice();

        if (target == playingDevice)
            return;

        if (!initialisedDevices.Contains(target))
        {
            if (!Bass.Init(target, 44100, DeviceInitFlags.Latency) && Bass.LastError != Errors.Already)
            {
                Logger.Error($"Could not open the audio device {nameOf(target)}.", new BassException(Bass.LastError));
                return;
            }

            initialisedDevices.Add(target);
        }

        if (!Bass.ChannelSetDevice(trackMixer.ChannelHandle, target) || !Bass.ChannelSetDevice(sampleMixer.ChannelHandle, target))
        {
            Logger.Error($"Could not play on the audio device {nameOf(target)}.", new BassException(Bass.LastError));
            return;
        }

        playingDevice = target;
        Bass.CurrentDevice = target;

        Logger.Verbose($"Playing on {nameOf(target)}");
    }

    private static string nameOf(int device) => Bass.GetDeviceInfo(device, out var info) ? info.Name : device.ToString();

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

    public Scheduler? EventScheduler { get; set; }

    public void RaiseEvent(Action action)
    {
        if (EventScheduler != null)
            EventScheduler.Add(action);
        else
            action();
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

        sinceDeviceCheckMs += frameTime;

        // A device plugged in or out: the list changes, and the mixers move if their device went, or the one the
        // player picked came back.
        if (sinceDeviceCheckMs >= device_check_interval_ms)
        {
            sinceDeviceCheckMs = 0;

            if (refreshOutputDevices())
            {
                RaiseEvent(() => OutputDevicesChanged?.Invoke());
                followPreferredDevice();
            }
        }

        GlobalStatistics.Get<double>("Audio", "BASS CPU Usage", StatisticKind.Gauge, StatisticUnit.Percent).Value = Bass.CPUUsage;

        // Named to line up with "SDL Output Latency (ms)" so the two backends can be read against each
        // other in the same overlay. Constant for the life of the device, published every frame anyway
        // because a statistic that only appears once is a statistic nobody sees.
        GlobalStatistics.Get<double>("Audio", "BASS Output Latency", StatisticKind.Gauge, StatisticUnit.Milliseconds).Value = OutputLatencyMs;
        GlobalStatistics.Get<int>("Audio", "BASS Playback Buffer", StatisticKind.Gauge, StatisticUnit.Milliseconds).Value = PlaybackBufferMs;
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

        foreach (int device in initialisedDevices)
        {
            Bass.CurrentDevice = device;
            Bass.Free();
        }
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
