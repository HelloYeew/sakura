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

    protected override ITrack CreateComponent(Stream stream)
    {
        return audioManager.CreateTrack(stream);
    }
}
