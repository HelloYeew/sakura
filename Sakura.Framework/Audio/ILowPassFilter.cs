// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Audio;

/// <summary>
/// A low-pass filter attached to an <see cref="IAudioChannel"/>, cut off frequencies above
/// <see cref="CutoffFrequency"/>. Obtained from <see cref="AudioChannelExtensions.AddLowPassFilter"/>.
/// </summary>
public interface ILowPassFilter : IDisposable
{
    /// <summary>
    /// The cutoff frequency a filter starts at in Hertz, and the value <see cref="Reset"/> returns to.
    /// </summary>
    static double DefaultCutoffFrequency => 20000.0;

    /// <summary>
    /// The cutoff frequency of the filter in Hertz. Frequencies above this value are reduced.
    /// </summary>
    /// <remarks>
    /// Backends clamp this to what the channel can express at most half the stream's sample rate
    /// (e.g., 22050 for a 44100Hz stream), so the value read back from the underlying filter may be
    /// lower than the value written here.
    /// </remarks>
    Reactive<double> CutoffFrequency { get; }

    /// <summary>
    /// Resets <see cref="CutoffFrequency"/> to <see cref="DefaultCutoffFrequency"/>.
    /// </summary>
    void Reset();
}
