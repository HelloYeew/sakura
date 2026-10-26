// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Utilities;

namespace Sakura.Framework.Timing;

/// <summary>
/// A framed clock that smooths a source whose time advances in coarse steps
/// (typically an audio track position, which only updates per audio buffer).
/// <para>
/// Between source updates, time advances continuously using a real-time reference at the
/// source's rate. The interpolated time is guaranteed to never drift more than
/// <see cref="AllowableErrorMilliseconds"/> from the source, and to never run backwards
/// while the source is playing forwards — small corrections are absorbed smoothly, large
/// discontinuities (seeks) snap.
/// </para>
/// </summary>
public class InterpolatingFramedClock : IFrameBasedClock, ISourceChangeableClock
{
    /// <summary>
    /// The maximum amount the interpolated time may deviate from the source before snapping
    /// back to it, at a rate of 1 (it scales with faster rates). Should be well over the source's
    /// update granularity: an audio output's callbacks come a burst apart, a few milliseconds on a
    /// phone's fast path, but 20 ms or more on others (Bluetooth), and every step past this is a
    /// visible jump. Two frames at 60 Hz.
    /// </summary>
    public double AllowableErrorMilliseconds { get; set; } = 1000.0 / 60 * 2;

    /// <summary>
    /// How quickly the interpolated time is drawn back to the source: half the gap closes in this
    /// many milliseconds. 0 turns it off, leaving the time to follow the reference clock alone
    /// between snaps.
    /// </summary>
    /// <remarks>
    /// Without it, whatever gap the first frame left (up to a step of the source) stayed for good, and
    /// the audio's clock running a touch faster or slower than the reference opened it further over a
    /// song until it snapped. Either way, input was judged off by that much.
    /// </remarks>
    public double DriftRecoveryHalfLife { get; set; } = 50;

    /// <summary>
    /// Whether the last frame was produced by smooth interpolation (true) or by snapping
    /// directly to the source (false). Diagnostic.
    /// </summary>
    public bool IsInterpolating { get; private set; }

    private IClock source;
    private readonly IClock reference;
    private double lastReferenceTime;
    private bool hasProcessed;

    /// <summary>
    /// Whether the time follows the source exactly until the source moves: after it was stopped, or snapped to. A
    /// source started or moved holds where it was put until its audio gets going (a seek's audio decoded, a callback
    /// or two), and running on ahead of it meanwhile only snapped back by as much once it was further than allowed.
    /// </summary>
    private bool pinned;

    /// <summary>
    /// Where the source was when the time was pinned to it: it has moved once it's further than
    /// <see cref="pin_tolerance"/> from there. Not compared exactly, as a source put back where it was reports it give or
    /// take a rounding, or a remeasured latency.
    /// </summary>
    private double pinnedSourceTime;

    private const double pin_tolerance = 0.5;

    /// <summary>
    /// Has the next frame take the source's time as it is, as the first frame does: for a seek, which a small one would
    /// otherwise be absorbed as the source's jitter, the time held back until the source caught up.
    /// </summary>
    internal void Resync() => resync = true;

    private bool resync;

    public IClock Source => source;

    public double CurrentTime { get; private set; }
    public double ElapsedFrameTime { get; private set; }
    public double FramesPerSecond => (source as IFrameBasedClock)?.FramesPerSecond ?? 0;
    public double Rate => source.Rate;
    public bool IsRunning => source.IsRunning;

    /// <param name="source">The coarse clock to smooth.</param>
    /// <param name="referenceClock">
    /// The real-time reference used to advance between source updates.
    /// Defaults to the shared <see cref="TimeSource"/> timeline; tests can pass a
    /// <see cref="ManualClock"/> for full determinism.
    /// </param>
    public InterpolatingFramedClock(IClock source, IClock? referenceClock = null)
    {
        this.source = source;
        reference = referenceClock ?? new TimeSourceClock();
        CurrentTime = source.CurrentTime;
        lastReferenceTime = reference.CurrentTime;
    }

    public void ChangeSource(IClock newSource)
    {
        source = newSource;
        CurrentTime = newSource.CurrentTime;
        lastReferenceTime = reference.CurrentTime;
        hasProcessed = false;
        pinned = false;
        resync = false;
    }

    public void ProcessFrame() => ProcessFrame(reference.CurrentTime);

    /// <summary>
    /// Processes a frame using an externally captured reference time, allowing a composed
    /// chain (e.g. <see cref="GameplayClock"/>) to take a single reference snapshot per frame
    /// and share it across all stages.
    /// </summary>
    /// <param name="referenceTime">The current time of this clock's reference, captured once for this frame.</param>
    internal void ProcessFrame(double referenceTime)
    {
        double lastTime = CurrentTime;
        double sourceTime = source.CurrentTime;
        double referenceElapsed = referenceTime - lastReferenceTime;
        lastReferenceTime = referenceTime;

        if (!source.IsRunning)
        {
            // A stopped source is exact by definition — report it directly.
            CurrentTime = sourceTime;
            IsInterpolating = false;
            pin(sourceTime);
        }
        else if (!hasProcessed || resync)
        {
            // First frame: no interpolation history yet. After a seek, held there until the source moves on, as after
            // a stop: it's put where it was asked and only moves once its audio does.
            CurrentTime = sourceTime;
            IsInterpolating = false;

            if (resync)
                pin(sourceTime);

            hasProcessed = true;
            resync = false;
        }
        else if (pinned && Math.Abs(sourceTime - pinnedSourceTime) <= pin_tolerance)
        {
            CurrentTime = source.Rate >= 0 ? Math.Max(lastTime, sourceTime) : Math.Min(lastTime, sourceTime);
            IsInterpolating = false;
        }
        else
        {
            pinned = false;

            double candidate = lastTime + referenceElapsed * source.Rate;

            // Drawn towards the source a little every frame. The source moves in steps, so the pull
            // averages them out rather than following each one.
            if (DriftRecoveryHalfLife > 0 && referenceElapsed > 0)
                candidate += (sourceTime - candidate) * (1 - Math.Pow(0.5, referenceElapsed / DriftRecoveryHalfLife));

            double allowableError = AllowableErrorMilliseconds * Math.Max(1, Math.Abs(source.Rate));

            if (Math.Abs(candidate - sourceTime) > allowableError)
            {
                // Too far from the source to be a buffer-granularity artifact —
                // treat as a discontinuity (seek / stall) and snap.
                CurrentTime = sourceTime;
                IsInterpolating = false;
                pin(sourceTime);
            }
            else
            {
                // Smooth advance, but never run backwards relative to the last frame
                // while playing forwards, and never lead the source by more than the
                // allowable error. Not a clamp: the pull can leave the last frame past the
                // lead allowed, and then holding still beats stepping back.
                if (source.Rate >= 0)
                    candidate = Math.Max(Math.Min(candidate, sourceTime + allowableError), lastTime);
                else
                    candidate = Math.Min(Math.Max(candidate, sourceTime - allowableError), lastTime);

                CurrentTime = candidate;
                IsInterpolating = true;
            }
        }

        ElapsedFrameTime = CurrentTime - lastTime;
    }

    private void pin(double sourceTime)
    {
        pinned = true;
        pinnedSourceTime = sourceTime;
    }

    /// <summary>
    /// Whether this clock currently reports (almost) exactly the source time.
    /// </summary>
    public bool IsAtSourceTime => Precision.AlmostEquals(CurrentTime, source.CurrentTime, AllowableErrorMilliseconds);

    public override string ToString() => $"InterpolatingFramedClock: {CurrentTime:F2}ms (source: {source.CurrentTime:F2}ms, interpolating: {IsInterpolating})";
}
