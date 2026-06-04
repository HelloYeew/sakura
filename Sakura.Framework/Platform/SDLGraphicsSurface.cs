// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.Diagnostics.CodeAnalysis;

namespace Sakura.Framework.Platform;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class SDLGraphicsSurface : IOpenGLGraphicsSurface
{
    public GraphicsSurfaceType Type => GraphicsSurfaceType.OpenGL;

    public Func<string, nint> GetFunctionAddress { get; set; }
    public Action MakeCurrent { get; set; }
    public Action ClearCurrent { get; set; }
}
