// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// A cursor over decoded audio, already in the device's format, that a voice pulls frames from.
/// </summary>
internal interface IPcmSource : IDisposable
{
    /// <summary>
    /// Total length in milliseconds, or 0 when the source does not know.
    /// </summary>
    double LengthMs { get; }

    /// <summary>
    /// The read cursor, in milliseconds. Reflects what has been handed to the mixer, so it runs
    /// ahead of what is audible by however many sits in the device buffer.
    /// </summary>
    double PositionMs { get; }

    /// <summary>
    /// Whether the source has been fully consumed. A streaming source is only ended once its
    /// decoder is drained <em>and</em> its buffer is empty.
    /// </summary>
    bool Ended { get; }

    /// <summary>
    /// Reads sequential interleaved frames, advancing the cursor.
    /// </summary>
    /// <returns>
    /// Frames written. A short read means either the end of the source or — for a streaming source —
    /// that the decoder has not kept up, which the caller should treat as an underrun rather than an
    /// end.
    /// </returns>
    int ReadFrames(Span<float> destination, int frameCount);

    /// <summary>
    /// Moves the cursor. Takes effect immediately as far as <see cref="PositionMs"/> is concerned,
    /// even where the underlying decoding happens asynchronously.
    /// </summary>
    void Seek(double milliseconds);
}
