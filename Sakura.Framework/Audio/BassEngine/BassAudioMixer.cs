// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using ManagedBass;
using ManagedBass.Mix;

namespace Sakura.Framework.Audio.BassEngine;

internal class BassAudioMixer : BassAudioChannel, IAudioMixer
{
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
                BassFlags.MixerChanDownMix | BassFlags.MixerChanPause), "adding channel to mixer");

            bassChannel.Mixer = this;
        }
    }

    public void RemoveChannel(IAudioChannel channel)
    {
        if (channel is BassAudioChannel bassChannel)
        {
            BassMix.MixerRemoveChannel(bassChannel.ChannelHandle);
            bassChannel.Mixer = null;
        }
    }
}
