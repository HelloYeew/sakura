// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.IO;

namespace Sakura.Framework.Audio;

/// <summary>
/// Dummy implementation of ITrack
/// </summary>
internal class Track : ITrack
{
    private readonly AudioManager manager;

    public double Length { get; } = 30000; // Simulate a 30-second track
    public double RestartPoint { get; set; }

    public Track(AudioManager manager, Stream stream)
    {
        this.manager = manager;
    }

    public Track(AudioManager manager, string path)
    {
        this.manager = manager;
    }

    public IAudioChannel Play()
    {
        var channel = new TrackChannel(this, manager);
        channel.Play();
        return channel;
    }
}
