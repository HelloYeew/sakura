// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.IO;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Audio;

/// <summary>
/// Main audio engine interface. Responsible for loading audio data and managing all playbacks.
/// </summary>
public interface IAudioManager
{
    /// <summary>
    /// Master volume for all audio playbacks, affect both tracks and samples.
    /// </summary>
    Reactive<double> MasterVolume { get; }

    /// <summary>
    /// Master volume for track playbacks that separately from samples but still affect by <see cref="MasterVolume"/>
    /// </summary>
    Reactive<double> TrackVolume { get; }

    /// <summary>
    /// Master volume for sample playbacks that separately from tracks but still affect by <see cref="MasterVolume"/>
    /// </summary>
    Reactive<double> SampleVolume { get; }

    /// <summary>
    /// Loads a track from a <see cref="Stream"/>
    /// </summary>
    /// <param name="stream">The stream to load from</param>
    /// <returns>The loaded <see cref="ITrack"/></returns>
    ITrack CreateTrack(Stream stream);

    /// <summary>
    /// Loads a sample from a <see cref="Stream"/>
    /// </summary>
    /// <param name="stream">The stream to load from</param>
    /// <returns>The loaded <see cref="ISample"/></returns>
    ISample CreateSample(Stream stream);

    /// <summary>
    /// Load a track from a precised file path
    /// </summary>
    /// <param name="path">The full path to the audio file</param>
    /// <returns>The loaded <see cref="ITrack"/></returns>
    ITrack CreateTrackFromFile(string path);

    /// <summary>
    /// Load a sample from a precised file path
    /// </summary>
    /// <param name="path">The full path to the audio file</param>
    /// <returns>The loaded <see cref="ISample"/></returns>
    ISample CreateSampleFromFile(string path);

    /// <summary>
    /// Updates the state of all playing audio channels.
    /// The host should call this once per frame.
    /// </summary>
    /// <param name="frameTime"></param>
    void Update(double frameTime);
}
