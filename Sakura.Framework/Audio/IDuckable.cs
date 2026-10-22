// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Audio;

/// <summary>
/// A channel whose sound something else can turn down for a while, under the volume its owner sets: what
/// <see cref="AudioDucker"/> turns down, without touching <see cref="IAudioChannel.Volume"/>.
/// </summary>
internal interface IDuckable
{
    /// <summary>
    /// What <see cref="IAudioChannel.Volume"/> is multiplied by, 0 to 1; 1 leaves it as it is.
    /// </summary>
    double Ducking { get; set; }
}
