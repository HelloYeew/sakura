// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Timing;

/// <summary>
/// A clock that can be used to limit a process to a specific frequency.
/// </summary>
public class ThrottledFrameClock
{
    private readonly double timeBetweenUpdates;
    private double lastProcessTime;

    /// <summary>
    /// Creates a new ThrottledFrameClock.
    /// </summary>
    /// <param name="targetFrequency">The target frequency in Hz.</param>
    public ThrottledFrameClock(int targetFrequency)
    {
        if (targetFrequency <= 0)
            timeBetweenUpdates = 0;
        else
            timeBetweenUpdates = 1000.0 / targetFrequency;
    }

    /// <summary>
    /// Checks if enough time has passed to process another frame.
    /// </summary>
    /// <param name="currentTime">The current time in milliseconds.</param>
    /// <returns>True if the process should run, false otherwise.</returns>
    public bool Process(double currentTime)
    {
        if (currentTime - lastProcessTime < timeBetweenUpdates)
            return false;

        // Adding a fixed interval to prevent drift over time
        // If the clock is really lacking behind, this will allow it to "catch up"
        // by processing multiple ticks in a row.
        lastProcessTime += timeBetweenUpdates;

        // Check to prevent a spiral of death if the processing is really slow.
        if (currentTime - lastProcessTime > timeBetweenUpdates * 10)
        {
            lastProcessTime = currentTime;
        }

        return true;
    }
}
