// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Utilities;

namespace Sakura.Framework.Timing;

/// <summary>
/// A framed clock that can keep running when its adjustable source cannot.
/// While the source is playing, this clock reports the source's time. While the source is
/// stopped (or the time is outside the source's valid range, e.g. negative lead-in time
/// before an audio track starts), the clock continues running from an internal real-time
/// reference. When the decoupled time re-enters the source's valid range, the source is
/// sought to the current time and started automatically.
/// A source that stops by itself while this clock is following it (a track reaching its end) is
/// left stopped, and the time carries on decoupled; it is only started again by an explicit
/// <see cref="Start"/> or <see cref="Seek"/>. Restarting it would replay a finished track from the
/// beginning.
/// </summary>
public class DecouplingFramedClock : IFrameBasedClock, IAdjustableClock, ISourceChangeableClock
{
    private IAdjustableClock source;
    private readonly IClock reference;
    private double lastReferenceTime;
    private bool isRunning;
    private double rate = 1.0;

    // Whether the last running frame took its time from the source.
    private bool followingSource;

    // Set when the source stopped on its own while being followed; blocks the automatic hand-over
    // until an explicit Start/Seek/ChangeSource.
    private bool sourceStoppedItself;

    public IClock Source => source;

    public double CurrentTime { get; private set; }
    public double ElapsedFrameTime { get; private set; }
    public double FramesPerSecond => 0;
    public bool IsRunning => isRunning;

    /// <summary>
    /// Whether time is currently being driven by the internal reference rather than the source.
    /// </summary>
    public bool IsDecoupled { get; private set; }

    public double Rate
    {
        get => rate;
        set
        {
            rate = value;
            source.Rate = value;
        }
    }

    /// <param name="source">The adjustable source (typically a track clock).</param>
    /// <param name="referenceClock">
    /// Real-time reference for decoupled operation. Defaults to the shared
    /// <see cref="TimeSource"/> timeline; tests can pass a <see cref="ManualClock"/>.
    /// </param>
    public DecouplingFramedClock(IAdjustableClock source, IClock? referenceClock = null)
    {
        this.source = source;
        reference = referenceClock ?? new TimeSourceClock();
        lastReferenceTime = reference.CurrentTime;
        CurrentTime = source.CurrentTime;
    }

    public void ChangeSource(IClock newSource)
    {
        if (newSource is not IAdjustableClock adjustable)
            throw new ArgumentException($"Source of a {nameof(DecouplingFramedClock)} must be an {nameof(IAdjustableClock)}.", nameof(newSource));

        source = adjustable;
        CurrentTime = adjustable.CurrentTime;
        clearSourceStop();
        // Reset the reference baseline so no stale elapsed from the old source
        // leaks into the first ProcessFrame with the new source.
        lastReferenceTime = reference.CurrentTime;
    }

    public void Start()
    {
        if (isRunning) return;

        isRunning = true;
        clearSourceStop();

        // Only a source that accepted the position is started: a track can't seek past its end, and
        // starting one sitting at its end replays it from the beginning.
        if (CurrentTime >= 0 && source.Seek(CurrentTime))
            source.Start();
        // If we're in negative (lead-in) time, ProcessFrame runs decoupled and will
        // start the source automatically when time crosses zero.
    }

    public void Stop()
    {
        if (!isRunning) return;

        isRunning = false;
        source.Stop();
    }

    public bool Seek(double position)
    {
        CurrentTime = position;
        clearSourceStop();

        if (position >= 0)
        {
            if (source.Seek(position) && isRunning && !source.IsRunning)
                source.Start();
        }
        else
        {
            source.Stop();
            source.Seek(0);
        }

        return true;
    }

    public void Reset()
    {
        Stop();
        Seek(0);
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
        double referenceElapsed = referenceTime - lastReferenceTime;
        lastReferenceTime = referenceTime;

        if (!isRunning)
        {
            ElapsedFrameTime = 0;
            return;
        }

        if (source.IsRunning)
        {
            // Source-driven: report the source's time directly.
            CurrentTime = source.CurrentTime;
            IsDecoupled = false;
            followingSource = true;
        }
        else
        {
            // We were following the source and it stopped without being told to: it ran out.
            if (followingSource)
            {
                followingSource = false;
                sourceStoppedItself = true;
            }

            // Decoupled: advance from the real-time reference at our rate.
            CurrentTime += referenceElapsed * rate;
            IsDecoupled = true;

            // In the source's valid range (e.g. lead-in time crossing zero) — hand over to the source,
            // if it takes the position. Not to one that ran out: starting a finished track replays it.
            if (!sourceStoppedItself && Precision.AlmostBigger(CurrentTime, 0) && source.Seek(Math.Max(0, CurrentTime)))
                source.Start();
        }

        ElapsedFrameTime = CurrentTime - lastTime;
    }

    private void clearSourceStop()
    {
        followingSource = false;
        sourceStoppedItself = false;
    }

    public override string ToString() => $"DecouplingFramedClock: {CurrentTime:F2}ms (decoupled: {IsDecoupled}, running: {isRunning})";
}
