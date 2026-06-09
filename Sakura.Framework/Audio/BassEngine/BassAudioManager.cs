// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
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
    static BassAudioManager()
    {
        loadNativeLibraries();
    }

    /// <summary>
    /// Pre-loads BASS native DLLs from the runtimes/ folder next to the assembly.
    /// This is necessary when Sakura.Framework is consumed via a project reference rather
    /// than a NuGet package, because MSBuild does not copy transitive NuGet native assets
    /// to the output root in that case.
    /// </summary>
    private static void loadNativeLibraries()
    {
        string rid = getRid();
        if (rid == null) return;

        string assemblyDir = Path.GetDirectoryName(typeof(BassAudioManager).Assembly.Location);
        if (assemblyDir == null) return;

        string nativeDir = Path.Combine(assemblyDir, "runtimes", rid, "native");
        if (!Directory.Exists(nativeDir)) return;

        string ext = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".dll"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? ".dylib"
            : ".so";

        foreach (string name in new[] { "bass", "bass_fx", "bassmix" })
        {
            string path = Path.Combine(nativeDir, name + ext);
            if (File.Exists(path) && !NativeLibrary.TryLoad(path, out _))
                Logger.Warning($"[BASS] Failed to pre-load native library: {path}");
        }
    }

    private static string getRid()
    {
        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        bool isOsx = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        bool isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

        return RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 when isWindows => "win-arm64",
            Architecture.X64 when isWindows => "win-x64",
            Architecture.X86 when isWindows => "win-x86",
            Architecture.Arm64 when isOsx => "osx-arm64",
            Architecture.X64 when isOsx => "osx-x64",
            Architecture.X64 when isLinux => "linux-x64",
            Architecture.Arm64 when isLinux => "linux-arm64",
            _ => null
        };
    }
    private readonly List<BassAudioChannel> activeChannels = new List<BassAudioChannel>();
    private readonly ConcurrentQueue<Action> audioThreadActions = new ConcurrentQueue<Action>();
    private readonly SyncProcedure channelEndSync;

    private readonly BassAudioMixer trackMixer;
    private readonly BassAudioMixer sampleMixer;

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
