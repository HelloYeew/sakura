// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Timing;

/// <summary>
/// A fully manual <see cref="IAdjustableClock"/> simulating an audio track:
/// time only advances when the test explicitly calls <see cref="AdvanceBy"/>,
/// mimicking the coarse buffer-step behavior of a real audio position.
/// </summary>
public class TestAdjustableClock : IAdjustableClock
{
    public double CurrentTime { get; private set; }

    public double Rate { get; set; } = 1.0;

    public bool IsRunning { get; private set; }

    public int SeekCount { get; private set; }

    public void Start() => IsRunning = true;

    public void Stop() => IsRunning = false;

    public bool Seek(double position)
    {
        CurrentTime = position;
        SeekCount++;
        return true;
    }

    public void Reset()
    {
        IsRunning = false;
        CurrentTime = 0;
    }

    /// <summary>
    /// Simulates audio playback progressing by the given amount of milliseconds.
    /// </summary>
    public void AdvanceBy(double milliseconds) => CurrentTime += milliseconds;
}
