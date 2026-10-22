// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Graphics.UserInterface;

/// <summary>
/// How a <see cref="TapCalibrationPad"/> gives the beat: one way only, so a run measures that way alone.
/// </summary>
public enum CalibrationCue
{
    /// <summary>
    /// A click to hear on every beat, with nothing to see: measures sound and touch.
    /// </summary>
    Sound,

    /// <summary>
    /// A flash to see on every beat, in silence: measures picture and touch.
    /// </summary>
    Flash
}
