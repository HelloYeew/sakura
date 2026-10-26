// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Reactive;
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
/// <para>
/// <see cref="Rate"/> keeps the pitch by default: it drives the channel's <see cref="IAudioChannel.Tempo"/>,
/// so a track played at half speed sounds slower, not an octave lower. Set
/// <see cref="PreservePitch"/> to false for the record-player behaviour instead, where speed and pitch
/// move together through <see cref="IAudioChannel.Frequency"/>.
/// </para>
/// </summary>
public class TrackClock : IAdjustableClock, IHasOutputLatency
{
    private readonly IAudioChannel channel;

    private bool preservePitch = true;

    public TrackClock(IAudioChannel channel)
    {
        this.channel = channel;
    }

    public double CurrentTime => channel.CurrentTime;

    public double OutputLatency => channel.OutputLatency;

    public bool IsRunning => channel.IsRunning.Value;

    /// <summary>
    /// Whether <see cref="Rate"/> changes the speed through <see cref="IAudioChannel.Tempo"/>, keeping
    /// the pitch (the default), or through <see cref="IAudioChannel.Frequency"/>, which resamples so the
    /// pitch rises and falls with the speed. The second is how this clock behaved before tempo existed,
    /// and what a slow-down-to-a-stop effect wants.
    /// </summary>
    /// <remarks>
    /// The clock owns whichever of the two properties this picks, and leaves the other alone. Switching
    /// carries the current rate across, so the clock's speed doesn't change, only how it is produced.
    /// </remarks>
    public bool PreservePitch
    {
        get => preservePitch;
        set
        {
            if (value == preservePitch)
                return;

            double rate = ownedAdjustment.Value;
            ownedAdjustment.Value = 1;

            preservePitch = value;
            ownedAdjustment.Value = rate;
        }
    }

    private Reactive<double> ownedAdjustment => preservePitch ? channel.Tempo : channel.Frequency;

    /// <summary>
    /// How fast the track plays. Setting it changes the property <see cref="PreservePitch"/> selects;
    /// reading it gives the speed the audio actually advances at, which is
    /// <see cref="IAudioChannel.Frequency"/> times <see cref="IAudioChannel.Tempo"/> whoever set them.
    /// </summary>
    public double Rate
    {
        get => channel.Frequency.Value * channel.Tempo.Value;
        set => ownedAdjustment.Value = value;
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

    public override string ToString() => $"TrackClock: {CurrentTime:F2}ms (running: {IsRunning}, rate: {Rate}, preserve pitch: {PreservePitch})";
}
