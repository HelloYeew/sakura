// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.IO;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Audio;

/// <summary>
/// A store for retrieving <see cref="ISample"/> instances from <see cref="Storage"/>
/// </summary>
public class SampleStore : AudioStore<ISample>
{
    private readonly IAudioManager audioManager;

    public SampleStore(Storage storage, IAudioManager audioManager) : base(storage, audioManager)
    {
        this.audioManager = audioManager;
    }

    protected override ISample CreateComponent(Stream stream)
    {
        return audioManager.CreateSample(stream);
    }
}
