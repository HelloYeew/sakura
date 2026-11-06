// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Timing;

/// <summary>
/// How a part of a delay is known. A part known more than one way has each flag: measured up to the device, and
/// reported by the system after it.
/// </summary>
[Flags]
public enum LatencySource
{
    /// <summary>
    /// Not known at all: nothing reports it, and the part has no figure.
    /// </summary>
    NotReported = 0,

    /// <summary>
    /// Measured as the output plays, from its own timestamps.
    /// </summary>
    Measured = 1 << 0,

    /// <summary>
    /// Counted: what the app itself holds, read as it is.
    /// </summary>
    Counted = 1 << 1,

    /// <summary>
    /// Worked out from the sizes of the buffers the sound waits in.
    /// </summary>
    FromBufferSizes = 1 << 2,

    /// <summary>
    /// Reported by the system or the device, and read again as it changes.
    /// </summary>
    ReportedBySystem = 1 << 3,

    /// <summary>
    /// Reported once, when the output started, and not read again.
    /// </summary>
    ReportedAtStart = 1 << 4
}
