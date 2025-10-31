// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Audio;

/// <summary>
/// Dummy implementation for a track audio channel
/// </summary>
internal class TrackChannel : AudioChannel
{
    private readonly Track track;

    public TrackChannel(Track track, AudioManager manager) : base(manager)
    {
        this.track = track;
        Length = track.Length;
        Looping = true; // Tracks often loop by default
    }

    public override void Play()
    {
        // When playing a track, respect its RestartPoint if looping and not just unpausing
        if (!IsRunning.Value && !IsPaused)
        {
            CurrentTime = Looping ? track.RestartPoint : 0;
        }
        base.Play();
    }

    public override void Stop()
    {
        base.Stop();
        CurrentTime = Looping ? track.RestartPoint : 0;
    }

    protected override void HandleLoop()
    {
        if (Looping)
        {
            CurrentTime = track.RestartPoint;
        }
        else
        {
            Stop();
        }
    }
}
