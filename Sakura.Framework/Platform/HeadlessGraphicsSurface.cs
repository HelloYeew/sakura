// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;

namespace Sakura.Framework.Platform;

public class HeadlessGraphicsSurface : IGraphicsSurface
{
    public Func<string, IntPtr> GetFunctionAddress { get; set; } = _ => nint.Zero;
}
