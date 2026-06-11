// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Timing;

/// <summary>
/// A clock whose time is set manually by hand.
/// Useful for deterministic tests, replays and fixed-step simulation: drive any clock chain
/// (or an entire scene) by assigning <see cref="CurrentTime"/> exact values per step.
/// </summary>
public class ManualClock : IClock
{
    public double CurrentTime { get; set; }

    public double Rate { get; set; } = 1.0;

    public bool IsRunning { get; set; } = true;

    public override string ToString() => $"ManualClock: {CurrentTime:F2}ms (Rate: {Rate}, Running: {IsRunning})";
}
