// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;

namespace Sakura.Framework.Audio.Headless;

internal class HeadlessAudioMixer : HeadlessAudioChannel, IAudioMixer
{
    private readonly List<IAudioChannel> channels = new List<IAudioChannel>();

    public HeadlessAudioMixer(double length) : base(length)
    {
    }

    public IEnumerable<IAudioChannel> ActiveChannels => channels;

    public void AddChannel(IAudioChannel channel)
    {
        if (!channels.Contains(channel))
            channels.Add(channel);
    }

    public void RemoveChannel(IAudioChannel channel)
    {
        channels.Remove(channel);
    }
}
