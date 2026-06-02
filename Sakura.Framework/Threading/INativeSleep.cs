// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Threading;

/// <summary>
/// Provides a platform-specific high-precision sleep primitive.
/// </summary>
internal interface INativeSleep : IDisposable
{
    /// <summary>
    /// Sleep for the given duration.
    /// </summary>
    /// <returns>True if sleep succeeded; false if it should fall back to <see cref="System.Threading.Thread.Sleep"/>.</returns>
    bool Sleep(TimeSpan duration);
}
