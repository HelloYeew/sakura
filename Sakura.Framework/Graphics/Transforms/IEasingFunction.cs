// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Graphics.Transforms;

/// <summary>
/// A parameterised easing curve that maps a normalised progress value in the range [0, 1]
/// to an eased value (usually, but not necessarily, also in [0, 1]).
/// </summary>
public interface IEasingFunction
{
    /// <summary>
    /// Evaluates the easing curve at the given normalised <paramref name="progress"/>.
    /// </summary>
    /// <param name="progress">Normalised progress, expected in the range [0, 1].</param>
    /// <returns>The eased value.</returns>
    double Apply(double progress);
}
