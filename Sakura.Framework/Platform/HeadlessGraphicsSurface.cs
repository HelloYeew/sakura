// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;

namespace Sakura.Framework.Platform;

/// <summary>
/// No-op graphics surface used by the headless renderer.
/// Implements <see cref="IOpenGLGraphicsSurface"/> so headless hosts can still
/// pass it to code that expects an OpenGL surface without crashing.
/// All function-address lookups return zero (no actual GL context).
/// </summary>
public class HeadlessGraphicsSurface : IOpenGLGraphicsSurface
{
    public GraphicsSurfaceType Type => GraphicsSurfaceType.OpenGL;

    public Func<string, nint> GetFunctionAddress { get; set; } = _ => nint.Zero;
    public Action MakeCurrent { get; set; } = () => { };
    public Action ClearCurrent { get; set; } = () => { };
}
