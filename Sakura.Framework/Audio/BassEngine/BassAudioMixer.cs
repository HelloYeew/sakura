// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using ManagedBass;
using ManagedBass.Mix;

namespace Sakura.Framework.Audio.BassEngine;

internal class BassAudioMixer : BassAudioChannel, IAudioMixer
{
    private readonly List<IAudioChannel> activeChannels = new List<IAudioChannel>();

    public BassAudioMixer(BassAudioManager manager) : base(BassMix.CreateMixerStream(44100, 2, BassFlags.Default | BassFlags.Float), manager, true, null)
    {
    }

    public void AddChannel(IAudioChannel channel)
    {
        if (channel is BassAudioChannel bassChannel)
        {
            // Add the channel in a paused state so it doesn't immediately play until Play() is called.
            // MixerChanDownMix ensures mono sounds play correctly in the stereo mixer.
            BassUtils.CheckError(BassMix.MixerAddChannel(
                ChannelHandle,
                bassChannel.ChannelHandle,
                BassFlags.MixerChanPause | BassFlags.MixerChanBuffer), "adding channel to mixer");

            bassChannel.Mixer = this;

            lock (activeChannels)
            {
                activeChannels.Add(channel);
            }
        }
    }

    public void RemoveChannel(IAudioChannel channel)
    {
        if (channel is BassAudioChannel bassChannel)
        {
            BassMix.MixerRemoveChannel(bassChannel.ChannelHandle);
            bassChannel.Mixer = null;

            lock (activeChannels)
            {
                activeChannels.Remove(channel);
            }
        }
    }

    public IEnumerable<IAudioChannel> ActiveChannels => activeChannels;
}
