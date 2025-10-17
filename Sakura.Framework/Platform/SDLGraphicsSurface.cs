// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.Diagnostics.CodeAnalysis;

namespace Sakura.Framework.Platform;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class SDLGraphicsSurface : IGraphicsSurface
{
    private Func<string, IntPtr> GetFunctionAddress { get; set; }

    Func<string, IntPtr> IGraphicsSurface.GetFunctionAddress
    {
        get => GetFunctionAddress;
        set => GetFunctionAddress = value;
    }
}
