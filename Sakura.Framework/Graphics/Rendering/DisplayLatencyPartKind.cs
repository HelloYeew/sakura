// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Graphics.Rendering;

/// <summary>
/// Where on its way to the screen a part of <see cref="IRenderer.DisplayLatencyMilliseconds"/> goes.
/// </summary>
public enum DisplayLatencyPartKind
{
    /// <summary>
    /// The update the frame shows, from its start to its end.
    /// </summary>
    Update,

    /// <summary>
    /// Waiting for the draw to take that update.
    /// </summary>
    WaitingForDraw,

    /// <summary>
    /// Drawing it, up to handing it over to the GPU and the display.
    /// </summary>
    Draw,

    /// <summary>
    /// The GPU's work on it. There only where the renderer can tell when that ended; elsewhere it's in
    /// <see cref="Presenting"/>.
    /// </summary>
    Gpu,

    /// <summary>
    /// From the GPU finishing (or the draw handing the frame over, where the GPU's time isn't known) to the frame being
    /// on screen: the present queue, the system's compositor and the scan-out.
    /// </summary>
    Presenting,

    /// <summary>
    /// The panel's own response, after the frame is on screen as far as the system can say. No software can measure it,
    /// so it never has a figure.
    /// </summary>
    Panel
}
