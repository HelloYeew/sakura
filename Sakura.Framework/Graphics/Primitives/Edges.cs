// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Graphics.Primitives;

/// <summary>
/// The edges of a rectangle, as flags.
/// </summary>
[Flags]
public enum Edges
{
    None = 0,
    Top = 1,
    Left = 1 << 1,
    Bottom = 1 << 2,
    Right = 1 << 3,

    Horizontal = Left | Right,
    Vertical = Top | Bottom,
    All = Top | Left | Bottom | Right,
}
