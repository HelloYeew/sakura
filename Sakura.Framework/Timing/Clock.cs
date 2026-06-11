// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Timing;

/// <summary>
/// A framed clock backed by the process-wide <see cref="TimeSource"/>.
/// A <see cref="Clock"/> that has never been stopped reports the shared timeline exactly,
/// meaning the times of any two such clocks are directly comparable across threads.
/// Stopping a clock excludes the paused duration from its reported time.
/// </summary>
public class Clock : IFrameBasedClock
{
    /// <summary>
    /// Total time excluded from the shared timeline due to this clock being stopped.
    /// </summary>
    private double pausedTotal;

    /// <summary>
    /// The shared-timeline time at which the clock last stopped (valid while stopped).
    /// </summary>
    private double stoppedAt;

    private int frames;
    private double fpsAccumulator;

    public double CurrentTime { get; private set; }
    public double ElapsedFrameTime { get; private set; }
    public double FramesPerSecond { get; private set; }
    public double Rate => 1.0;
    public bool IsRunning { get; private set; }

    public Clock(bool start = false)
    {
        stoppedAt = TimeSource.CurrentTime;
        if (start)
            Start();
    }

    public void Start()
    {
        if (IsRunning) return;

        pausedTotal += TimeSource.CurrentTime - stoppedAt;
        // Anchor so the first frame after starting doesn't report a huge elapsed time.
        CurrentTime = TimeSource.CurrentTime - pausedTotal;
        IsRunning = true;
    }

    public void Stop()
    {
        if (!IsRunning) return;

        stoppedAt = TimeSource.CurrentTime;
        IsRunning = false;
    }

    /// <summary>
    /// Takes this frame's time snapshot. Call once per frame.
    /// </summary>
    public void ProcessFrame()
    {
        if (!IsRunning)
        {
            ElapsedFrameTime = 0;
            return;
        }

        double newTime = TimeSource.CurrentTime - pausedTotal;
        ElapsedFrameTime = newTime - CurrentTime;
        CurrentTime = newTime;

        frames++;
        fpsAccumulator += ElapsedFrameTime;
        if (fpsAccumulator >= 1000)
        {
            FramesPerSecond = frames / (fpsAccumulator / 1000.0);
            frames = 0;
            fpsAccumulator -= 1000;
        }
    }

    /// <summary>
    /// Alias of <see cref="ProcessFrame"/>, kept for existing call sites.
    /// </summary>
    public void Update() => ProcessFrame();

    public override string ToString()
    {
        return $"Clock: {CurrentTime:F2}ms (FPS: {FramesPerSecond:F2})";
    }
}
