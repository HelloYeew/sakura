// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Timing;

/// <summary>
/// A clock that is "framed" to a source clock.
/// It processes time from its source, allow for local time manipulations like rate changes and pauses.
/// </summary>
public class FramedClock : IClock
{
    private readonly IClock source;
    private double lastSourceTime;

    public double CurrentTime { get; private set; }
    public double ElapsedFrameTime { get; private set; }
    public double Rate { get; set; } = 1.0;
    public bool IsRunning { get; private set; } = true;

    public double FramesPerSecond => source.FramesPerSecond;

    public FramedClock(IClock source)
    {
        this.source = source;
        CurrentTime = source.CurrentTime;
        lastSourceTime = source.CurrentTime;
    }

    public void Start() => IsRunning = true;
    public void Stop() => IsRunning = false;

    /// <summary>
    /// Updates the clock's current time based on the source clock.
    /// This should be called once per frame.
    /// </summary>
    public void Update()
    {
        if (IsRunning)
        {
            double sourceElapsed = source.CurrentTime - lastSourceTime;
            ElapsedFrameTime = sourceElapsed * Rate;
            CurrentTime += ElapsedFrameTime;
        }
        else
        {
            ElapsedFrameTime = 0;
        }
        lastSourceTime = source.CurrentTime;
    }

    public void ProcessFrame() => Update();

    public override string ToString() => $"FramedClock: {CurrentTime:F2}ms (Rate: {Rate}, Running: {IsRunning})";
}
