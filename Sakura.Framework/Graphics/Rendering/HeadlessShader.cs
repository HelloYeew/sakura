// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Rendering;

public sealed class HeadlessShader : IShader
{
    public uint Handle => 0;

    public void Use() { }
    public void SetUniform(string name, int value) { }
    public void SetUniform(string name, float value) { }
    public void SetUniform(string name, bool value) { }
    public void SetUniform(string name, Matrix4x4 value) { }
    public void SetUniform(string name, Vector2 value) { }
    public void SetUniform(string name, Vector4 value) { }
    public void SetUniform(string name, Color value) { }
    public void SetUniformIntArray(string name, int[] values) { }
    public void SetUniform(string name, float[] mat3X3) { }
    public void Dispose() { }
}
