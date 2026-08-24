// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// Something the decode thread keeps topped up like a track's buffer, wherever that buffer lives.
/// </summary>
internal interface IDecodeSource
{
    /// <summary>
    /// Whether the decode thread should spend time on this source right now.
    /// </summary>
    bool WantsDecode { get; }

    /// <summary>
    /// Does one unit of decoding work. Called only from the decode thread.
    /// </summary>
    /// <returns>True if there is more to do for this source right now.</returns>
    bool PumpDecode();
}
