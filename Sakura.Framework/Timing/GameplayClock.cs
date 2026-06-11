// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Timing;

/// <summary>
/// The recommended master clock for rhythm gameplay.
/// <para>
/// Composes the full precision chain over an audio source:
/// source → <see cref="DecouplingFramedClock"/> (lead-in/pause/seek support) →
/// <see cref="InterpolatingFramedClock"/> (smooths the coarse audio position) →
/// a per-frame snapshot with a user <see cref="Offset"/> applied.
/// </para>
/// <para>
/// Call <see cref="ProcessFrame"/> once per update frame (e.g. in your gameplay screen's
/// <c>Update</c>), then read <see cref="CurrentTime"/> everywhere. To judge an input event,
/// use <see cref="GetTimeAt"/> with the event's shared-timeline timestamp, this removes
/// the latency between the physical press and the frame that processes it.
/// </para>
/// </summary>
public class GameplayClock : IFrameBasedClock, IAdjustableClock
{
    private readonly DecouplingFramedClock decoupled;
    private readonly InterpolatingFramedClock interpolated;
    private double lastReferenceTime;
    private readonly IClock reference;

    /// <summary>
    /// A constant offset in milliseconds added to the reported time, used for user audio
    /// calibration (positive values make events register as earlier relative to the music).
    /// </summary>
    public double Offset { get; set; }

    /// <summary>
    /// The decoupling stage, exposed for diagnostics.
    /// </summary>
    public DecouplingFramedClock DecoupledClock => decoupled;

    /// <summary>
    /// The interpolation stage, exposed for diagnostics and error tuning
    /// (<see cref="InterpolatingFramedClock.AllowableErrorMilliseconds"/>).
    /// </summary>
    public InterpolatingFramedClock InterpolatedClock => interpolated;

    public double CurrentTime => interpolated.CurrentTime + Offset;
    public double ElapsedFrameTime => interpolated.ElapsedFrameTime;
    public double FramesPerSecond => 0;
    public bool IsRunning => decoupled.IsRunning;

    public double Rate
    {
        get => decoupled.Rate;
        set => decoupled.Rate = value;
    }

    /// <param name="source">The adjustable audio source clock (e.g. a <see cref="Sakura.Framework.Audio.TrackClock"/>).</param>
    /// <param name="referenceClock">
    /// Real-time reference for both stages. Defaults to the shared <see cref="TimeSource"/>
    /// timeline; tests can pass a <see cref="ManualClock"/> for determinism.
    /// </param>
    public GameplayClock(IAdjustableClock source, IClock? referenceClock = null)
    {
        reference = referenceClock ?? new TimeSourceClock();
        decoupled = new DecouplingFramedClock(source, reference);
        interpolated = new InterpolatingFramedClock(decoupled, reference);
        lastReferenceTime = reference.CurrentTime;
    }

    public void ProcessFrame()
    {
        lastReferenceTime = reference.CurrentTime;
        decoupled.ProcessFrame();
        interpolated.ProcessFrame();
    }

    /// <summary>
    /// Translates a timestamp on the shared <see cref="TimeSource"/> timeline (e.g. an input
    /// event's <c>Timestamp</c>) into this clock's gameplay time, compensating for the delay
    /// between when the event physically happened and the current frame.
    /// </summary>
    /// <param name="sharedTimelineTime">A time on the shared timeline, in milliseconds.</param>
    /// <returns>The gameplay time at which the event occurred.</returns>
    public double GetTimeAt(double sharedTimelineTime)
        => CurrentTime - (lastReferenceTime - sharedTimelineTime) * Rate;

    public void Start() => decoupled.Start();
    public void Stop() => decoupled.Stop();
    public bool Seek(double position) => decoupled.Seek(position);
    public void Reset() => decoupled.Reset();

    public override string ToString() => $"GameplayClock: {CurrentTime:F2}ms (offset: {Offset}, rate: {Rate}, running: {IsRunning})";
}
