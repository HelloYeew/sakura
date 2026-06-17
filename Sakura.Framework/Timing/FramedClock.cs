// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Timing;

/// <summary>
/// A clock that is "framed" to a source clock.
/// It processes time from its source once per frame, allowing local time manipulations
/// like rate changes and pauses. All times are in milliseconds.
/// </summary>
public class FramedClock : IFrameBasedClock, ISourceChangeableClock
{
    private IClock source;
    private double lastSourceTime;

    public IClock Source
    {
        get => source;
        set
        {
            source = value;
            lastSourceTime = source.CurrentTime;
        }
    }

    public void ChangeSource(IClock newSource) => Source = newSource;

    public double CurrentTime { get; private set; }
    public double ElapsedFrameTime { get; private set; }
    public double Rate { get; set; } = 1.0;
    public bool IsRunning { get; private set; } = true;

    public double FramesPerSecond => (source as IFrameBasedClock)?.FramesPerSecond ?? 0;

    public FramedClock(IClock source, bool startFromZero = false)
    {
        this.source = source;
        CurrentTime = startFromZero ? 0 : source.CurrentTime;
        lastSourceTime = source.CurrentTime;
    }

    public void Start() => IsRunning = true;
    public void Stop() => IsRunning = false;

    /// <summary>
    /// Rebases this clock so <see cref="CurrentTime"/> becomes 0, set based line onto the source's
    /// current time. The next <see cref="ProcessFrame"/> reports elapsed time measured from this
    /// point rather than producing a large jump
    /// </summary>
    public void Reset()
    {
        CurrentTime = 0;
        ElapsedFrameTime = 0;
        lastSourceTime = source.CurrentTime;
    }

    /// <summary>
    /// Updates the clock's current time based on the source clock.
    /// This should be called once per frame.
    /// </summary>
    public void ProcessFrame()
    {
        // Read the source exactly once per frame: live sources (stopwatch, audio track)
        // pay a time-source or audio-engine query per read, and a single snapshot also
        // guarantees the elapsed computation and the stored baseline can't tear.
        double sourceTime = source.CurrentTime;

        if (IsRunning)
        {
            ElapsedFrameTime = (sourceTime - lastSourceTime) * Rate;
            CurrentTime += ElapsedFrameTime;
        }
        else
        {
            ElapsedFrameTime = 0;
        }

        lastSourceTime = sourceTime;
    }

    /// <summary>
    /// Alias of <see cref="ProcessFrame"/>, kept for existing call sites.
    /// </summary>
    public void Update() => ProcessFrame();

    public override string ToString() => $"FramedClock: {CurrentTime:F2}ms (Rate: {Rate}, Running: {IsRunning})";
}
