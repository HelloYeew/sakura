// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.IO;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Audio;

/// <summary>
/// Dummy implementation of IAudioManager.
/// Manages a list of active channels and simulates audio playback.
/// </summary>
internal class AudioManager : IAudioManager
{
    private readonly List<AudioChannel> activeChannels = new List<AudioChannel>();
    private readonly List<AudioChannel> channelsToAdd = new List<AudioChannel>();
    private readonly List<AudioChannel> channelsToRemove = new List<AudioChannel>();

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

    public ISample CreateSampleFromFile(string path)
    {
        Logger.Debug($"[AudioManager] Creating dummy Sample from file: {path}");
        return new Sample(this, path);
    }

    internal void AddChannel(AudioChannel channel)
    {
        channelsToAdd.Add(channel);
    }

    internal void RemoveChannel(AudioChannel channel)
    {
        channelsToRemove.Add(channel);
    }

    public void Update(double frameTime)
    {
        // Add pending channels
        if (channelsToAdd.Count > 0)
        {
            foreach (var channel in channelsToAdd)
                activeChannels.Add(channel);
            channelsToAdd.Clear();
        }

        // Remove pending channels
        if (channelsToRemove.Count > 0)
        {
            foreach (var channel in channelsToRemove)
                activeChannels.Remove(channel);
            channelsToRemove.Clear();
        }

        // Update active channels
        foreach (var channel in activeChannels)
        {
            channel.Update(frameTime);
        }
    }
}
