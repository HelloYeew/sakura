// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Graphics.Primitives;

/// <summary>
/// Axes enumeration
/// </summary>
[Flags]
public enum Axes
{
    None = 0,
    X = 1 << 0,
    Y = 1 << 1,
    Both = X | Y
}
