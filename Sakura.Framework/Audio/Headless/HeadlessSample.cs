// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.IO;

namespace Sakura.Framework.Audio.Headless;

public class HeadlessSample : ISample
{
    private readonly HeadlessAudioManager manager;
    public double Length => 1000;

    public HeadlessSample(HeadlessAudioManager manager, Stream stream)
    {
        this.manager = manager;
    }

    public HeadlessSample(HeadlessAudioManager manager, string path)
    {
        this.manager = manager;
    }

    public IAudioChannel Play()
    {
        var channel = new HeadlessAudioChannel(Length)
        {
            Looping = false // Samples usually don't loop
        };

        manager.RegisterChannel(channel);

        channel.Play();
        return channel;
    }

    public void Dispose()
    {
        // Nothing to dispose
    }
}
