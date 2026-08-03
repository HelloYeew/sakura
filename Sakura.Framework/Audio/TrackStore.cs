// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.IO;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Audio;

/// <summary>
/// A store for retrieving <see cref="ITrack"/> instances from <see cref="Storage"/>
/// </summary>
public class TrackStore : AudioStore<ITrack>
{
    private readonly IAudioManager audioManager;

    public TrackStore(Storage storage, IAudioManager audioManager) : base(storage, audioManager)
    {
        this.audioManager = audioManager;
    }

    protected override int MaxCachedComponents => 10;

    protected override ITrack CreateComponent(Stream stream)
    {
        return audioManager.CreateTrack(stream);
    }

    /// <summary>
    /// Tracks are long, few, and played from start to end, so letting the audio backend stream them
    /// off disk is strictly better than holding the encoded file in memory for as long as the track
    /// is cached — <see cref="MaxCachedComponents"/> of those adds up to tens of megabytes.
    /// </summary>
    protected override ITrack? CreateComponent(string filePath)
    {
        return audioManager.CreateTrackFromFile(filePath);
    }
}
