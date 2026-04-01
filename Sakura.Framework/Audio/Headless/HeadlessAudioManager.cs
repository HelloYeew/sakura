// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.IO;
using Sakura.Framework.Logging;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Audio.Headless;

public class HeadlessAudioManager : IAudioManager, IDisposable
{
    public Reactive<double> MasterVolume { get; } = new Reactive<double>(1.0);
    public Reactive<double> TrackVolume { get; } = new Reactive<double>(1.0);
    public Reactive<double> SampleVolume { get; } = new Reactive<double>(1.0);

    private readonly List<HeadlessAudioChannel> activeChannels = new List<HeadlessAudioChannel>();

    public ITrack CreateTrack(Stream stream) => new HeadlessTrack(this, stream);
    public ITrack CreateTrackFromFile(string path) => new HeadlessTrack(this, path);

    public ISample CreateSample(Stream stream) => new HeadlessSample(this, stream);
    public ISample CreateSampleFromFile(string path) => new HeadlessSample(this, path);

    public IAudioMixer TrackMixer { get; } = new HeadlessAudioMixer(1000);

    public IAudioMixer SampleMixer { get; } = new HeadlessAudioMixer(1000);

    public HeadlessAudioManager()
    {
        TrackVolume.ValueChanged += e => TrackMixer.Volume.Value = e.NewValue * MasterVolume.Value;
        SampleVolume.ValueChanged += e => SampleMixer.Volume.Value = e.NewValue * MasterVolume.Value;
        MasterVolume.ValueChanged += e =>
        {
            TrackMixer.Volume.Value = TrackVolume.Value * e.NewValue;
            SampleMixer.Volume.Value = SampleVolume.Value * e.NewValue;
        };
        Logger.Verbose("🔈 Headless audio manager initialized");
    }

    public void Update(double frameTime)
    {
        for (int i = activeChannels.Count - 1; i >= 0; i--)
        {
            var channel = activeChannels[i];
            channel.Update(frameTime);
        }
    }

    internal void RegisterChannel(HeadlessAudioChannel channel)
    {
        activeChannels.Add(channel);
    }

    public void StopAll()
    {
        foreach (var channel in activeChannels)
        {
            channel.Stop();
        }
    }

    public void Dispose()
    {
        foreach (var channel in activeChannels)
        {
            channel.Dispose();
        }
        activeChannels.Clear();
    }
}
