// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Platform;

/// <summary>
/// What kind of application a host runs, which decides how much CPU its threads spend on timing precision.
/// </summary>
public enum AppType
{
    /// <summary>
    /// Pacing and input latency come first. Input and audio tick at 1000 Hz, the update thread runs at twice the
    /// draw rate, the update and draw threads busy-spin onto their deadlines, and the GC avoids blocking collections.
    /// </summary>
    Game,

    /// <summary>
    /// A UI application, such as a tool or a viewer, where nothing is gained by being sub-millisecond precise.
    /// Every thread ticks at the display rate and sleeps rather than spins, the default frame limiter is
    /// <see cref="FrameSync.VSync"/>, and the GC is left in its default mode, which keeps memory lower.
    /// </summary>
    Application,
}
