// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Audio;

/// <summary>
/// Dummy implementation for a sample audio channel
/// </summary>
internal class SampleChannel : AudioChannel
{
    private readonly Sample sample;

    public SampleChannel(Sample sample, AudioManager manager) : base(manager)
    {
        this.sample = sample;
        Length = sample.Length;
        Looping = false; // Samples rarely loop by default
    }

    protected override void HandleLoop()
    {
        // Samples just stop when they end, even if "Looping" was true
        Stop();
    }
}
