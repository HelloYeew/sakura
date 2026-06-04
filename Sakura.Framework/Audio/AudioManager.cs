// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Sakura.Framework.Logging;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Audio;

/// <summary>
/// Dummy implementation of IAudioManager.
/// Manages a list of active channels and simulates audio playback.
/// </summary>
internal class AudioManager : IAudioManager
{
    private readonly List<AudioChannel> activeChannels = new List<AudioChannel>();
    private readonly ConcurrentQueue<Action> audioThreadActions = new ConcurrentQueue<Action>();

    public Reactive<double> MasterVolume { get; } = new Reactive<double>(1.0);
    public Reactive<double> TrackVolume { get; } = new Reactive<double>(1.0);
    public Reactive<double> SampleVolume { get; } = new Reactive<double>(1.0);
    public IAudioMixer TrackMixer { get; } = new AudioMixer();
    public IAudioMixer SampleMixer { get; } = new AudioMixer();

    public ITrack CreateTrack(Stream stream)
    {
        Logger.Debug("Creating dummy Track from stream.");
        return new Track(this, stream);
    }

    public ISample CreateSample(Stream stream)
    {
        Logger.Debug($"[AudioManager] Creating dummy Sample from stream.");
        return new Sample(this, stream);
    }

    public ITrack CreateTrackFromFile(string path)
    {
        Logger.Debug($"[AudioManager] Creating dummy Track from file: {path}");
        return new Track(this, path);
    }
    public void EnqueueAction(Action action)
    {
        if (action != null)
        {
            audioThreadActions.Enqueue(action);
        }
    }

    public ISample CreateSampleFromFile(string path)
    {
        Logger.Debug($"[AudioManager] Creating dummy Sample from file: {path}");
        return new Sample(this, path);
    }

    internal void AddChannel(AudioChannel channel)
    {
        EnqueueAction(() =>
        {
            if (!activeChannels.Contains(channel))
                activeChannels.Add(channel);
        });
    }

    internal void RemoveChannel(AudioChannel channel)
    {
        EnqueueAction(() => activeChannels.Remove(channel));
    }

    public void Update(double frameTime)
    {
        while (audioThreadActions.TryDequeue(out var action))
        {
            action.Invoke();
        }

        // Update active channels
        foreach (var channel in activeChannels)
        {
            channel.Update(frameTime);
        }
    }

    public void StopAll()
    {
        foreach (var channel in activeChannels)
        {
            channel.Stop();
        }
    }
}
