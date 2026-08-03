// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.IO;

/// <summary>
/// A run of bytes living outside the managed heap, exposed as a pointer that stays valid until this is
/// disposed. Handed to native libraries that keep the pointer for as long as they hold the data.
/// </summary>
public interface INativeBytes : IDisposable
{
    /// <summary>
    /// Pointer to the first byte, valid until <see cref="IDisposable.Dispose"/>.
    /// </summary>
    IntPtr Pointer { get; }

    /// <summary>
    /// Number of valid bytes at <see cref="Pointer"/>.
    /// </summary>
    long Length { get; }
}
