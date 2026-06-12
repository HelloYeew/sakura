// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Timing;

/// <summary>
/// A live, always-running view of the shared <see cref="TimeSource"/> timeline.
/// Used as the default real-time reference for interpolating and decoupling clocks;
/// tests can substitute a <see cref="ManualClock"/> to make those clocks fully deterministic.
/// </summary>
public sealed class TimeSourceClock : IClock
{
    public double CurrentTime => TimeSource.CurrentTime;

    public double Rate => 1.0;

    public bool IsRunning => true;
}
