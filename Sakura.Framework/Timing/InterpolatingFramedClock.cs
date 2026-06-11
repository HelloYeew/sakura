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
    /// back to it. Should be at least the source's update granularity
    /// (one audio buffer, typically 5–10 ms).
    /// </summary>
    public double AllowableErrorMilliseconds { get; set; } = 10;

    /// <summary>
    /// Whether the last frame was produced by smooth interpolation (true) or by snapping
    /// directly to the source (false). Diagnostic.
    /// </summary>
    public bool IsInterpolating { get; private set; }

    private IClock source;
    private readonly IClock reference;
    private double lastReferenceTime;
    private bool hasProcessed;

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
    }

    public void ProcessFrame()
    {
        double lastTime = CurrentTime;
        double sourceTime = source.CurrentTime;
        double referenceTime = reference.CurrentTime;
        double referenceElapsed = referenceTime - lastReferenceTime;
        lastReferenceTime = referenceTime;

        if (!source.IsRunning)
        {
            // A stopped source is exact by definition — report it directly.
            CurrentTime = sourceTime;
            IsInterpolating = false;
        }
        else if (!hasProcessed)
        {
            // First frame: no interpolation history yet.
            CurrentTime = sourceTime;
            IsInterpolating = false;
            hasProcessed = true;
        }
        else
        {
            double candidate = lastTime + referenceElapsed * source.Rate;

            if (Math.Abs(candidate - sourceTime) > AllowableErrorMilliseconds)
            {
                // Too far from the source to be a buffer-granularity artifact —
                // treat as a discontinuity (seek / stall) and snap.
                CurrentTime = sourceTime;
                IsInterpolating = false;
            }
            else
            {
                // Smooth advance, but never run backwards relative to the last frame
                // while playing forwards, and never lead the source by more than the
                // allowable error.
                if (source.Rate >= 0)
                    candidate = Math.Clamp(candidate, lastTime, sourceTime + AllowableErrorMilliseconds);
                else
                    candidate = Math.Clamp(candidate, sourceTime - AllowableErrorMilliseconds, lastTime);

                CurrentTime = candidate;
                IsInterpolating = true;
            }
        }

        ElapsedFrameTime = CurrentTime - lastTime;
    }

    /// <summary>
    /// Whether this clock currently reports (almost) exactly the source time.
    /// </summary>
    public bool IsAtSourceTime => Precision.AlmostEquals(CurrentTime, source.CurrentTime, AllowableErrorMilliseconds);

    public override string ToString() => $"InterpolatingFramedClock: {CurrentTime:F2}ms (source: {source.CurrentTime:F2}ms, interpolating: {IsInterpolating})";
}
