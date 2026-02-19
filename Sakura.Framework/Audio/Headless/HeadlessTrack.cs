// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.IO;

namespace Sakura.Framework.Audio.Headless;

public class HeadlessTrack : ITrack
{
    private readonly HeadlessAudioManager manager;
    public double Length => 180000;
    public double RestartPoint { get; set; }

    public HeadlessTrack(HeadlessAudioManager manager, Stream stream)
    {
        this.manager = manager;
    }

    public HeadlessTrack(HeadlessAudioManager manager, string path)
    {
        this.manager = manager;
    }

    public IAudioChannel Play()
    {
        var channel = new HeadlessAudioChannel(Length)
        {
            Looping = true,
            RestartPoint = RestartPoint
        };

        manager.RegisterChannel(channel);

        channel.Play();
        return channel;
    }

    public void Dispose()
    {

    }
}
