// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Maths;
using Silk.NET.OpenGL;

namespace Sakura.Framework.Extensions.GLExtensions;

public static class GLExtensions
{
    public static void ClearColor(this GL gl, Vector4 color)
    {
        gl.ClearColor(color.X, color.Y, color.Z, color.W);
    }
}
