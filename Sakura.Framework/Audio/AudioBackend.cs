// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Diagnostics.CodeAnalysis;

namespace Sakura.Framework.Audio;

/// <summary>
/// The audio backend an app plays sound through
/// </summary>
public enum AudioBackend
{
    /// <summary>
    /// Let the framework pick
    /// </summary>
    Automatic,

    /// <summary>
    /// The BASS backend
    /// </summary>
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    BASS,

    /// <summary>
    /// The SDL3 audio backend, mixing in libsakura-audio where it is available.
    /// </summary>
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    SDL,

    /// <summary>
    /// The SDL3 audio backend with its managed reference mixer, never the native one.
    /// </summary>
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    SDLManaged
}
