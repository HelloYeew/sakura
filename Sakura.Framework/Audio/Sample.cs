// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.IO;

namespace Sakura.Framework.Audio;

/// <summary>
/// Dummy implementation of ISample.
/// </summary>
internal class Sample : ISample
{
    private readonly AudioManager manager;

    public double Length { get; } = 1500;

    public Sample(AudioManager manager, Stream stream)
    {
        this.manager = manager;
    }

    public Sample(AudioManager manager, string path)
    {
        this.manager = manager;
    }

    public IAudioChannel Play()
    {
        var channel = new SampleChannel(this, manager);
        channel.Play();
        return channel;
    }
}
