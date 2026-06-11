// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Timing;

/// <summary>
/// An adjustable clock running off the shared <see cref="TimeSource"/>.
/// Supports start/stop, seeking and rate adjustment with no accumulation error:
/// the current time is always derived directly from the time source.
/// </summary>
public class StopwatchClock : IAdjustableClock
{
    private double accumulated;
    private double lastReferenceTime;
    private double rate = 1.0;

    public bool IsRunning { get; private set; }

    public double CurrentTime => IsRunning
        ? accumulated + (TimeSource.CurrentTime - lastReferenceTime) * rate
        : accumulated;

    public double Rate
    {
        get => rate;
        set
        {
            if (rate == value) return;

            // Bank the time elapsed at the old rate before switching.
            accumulated = CurrentTime;
            lastReferenceTime = TimeSource.CurrentTime;
            rate = value;
        }
    }

    public StopwatchClock(bool start = false)
    {
        if (start)
            Start();
    }

    public void Start()
    {
        if (IsRunning) return;

        lastReferenceTime = TimeSource.CurrentTime;
        IsRunning = true;
    }

    public void Stop()
    {
        if (!IsRunning) return;

        accumulated = CurrentTime;
        IsRunning = false;
    }

    public bool Seek(double position)
    {
        accumulated = position;
        lastReferenceTime = TimeSource.CurrentTime;
        return true;
    }

    public void Reset()
    {
        Stop();
        accumulated = 0;
    }

    public override string ToString() => $"StopwatchClock: {CurrentTime:F2}ms (Rate: {Rate}, Running: {IsRunning})";
}
