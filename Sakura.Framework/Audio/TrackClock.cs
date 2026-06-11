// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Timing;

namespace Sakura.Framework.Audio;

/// <summary>
/// A track clock that use for precise timing.
/// Adapts an <see cref="IAudioChannel"/> (typically a music track) to the
/// <see cref="IAdjustableClock"/> contract so it can drive the timing clock chain
/// (<see cref="DecouplingFramedClock"/> → <see cref="InterpolatingFramedClock"/> /
/// <see cref="GameplayClock"/>).
/// The reported time is the audio engine's playback position, which advances in coarse
/// steps (per audio buffer) — always wrap this clock in the chain above rather than
/// reading it directly for gameplay.
/// </summary>
public class TrackClock : IAdjustableClock
{
    private readonly IAudioChannel channel;

    public TrackClock(IAudioChannel channel)
    {
        this.channel = channel;
    }

    public double CurrentTime => channel.CurrentTime;

    public bool IsRunning => channel.IsRunning.Value;

    public double Rate
    {
        get => channel.Frequency.Value;
        set => channel.Frequency.Value = value;
    }

    public void Start()
    {
        if (!channel.IsRunning.Value)
            channel.Play();
    }

    public void Stop()
    {
        if (channel.IsRunning.Value)
            channel.Pause();
    }

    public bool Seek(double position)
    {
        if (position < 0 || position > channel.Length)
            return false;

        channel.CurrentTime = position;
        return true;
    }

    public void Reset()
    {
        Stop();
        channel.CurrentTime = 0;
    }

    public override string ToString() => $"TrackClock: {CurrentTime:F2}ms (running: {IsRunning}, rate: {Rate})";
}
