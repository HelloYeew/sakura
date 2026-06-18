// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Rendering;

/// <summary>
/// Backend-agnostic shader program abstraction.
/// </summary>
public interface IShader : IDisposable
{
    /// <summary>
    /// Activates this shader program for subsequent draw calls.
    /// </summary>
    void Use();

    void SetUniform(string name, int value);
    void SetUniform(string name, float value);
    void SetUniform(string name, bool value);
    void SetUniform(string name, Matrix4x4 value);
    void SetUniform(string name, Vector2 value);
    void SetUniform(string name, Vector4 value);
    void SetUniform(string name, Color value);
    void SetUniformIntArray(string name, int[] values);

    /// <summary>
    /// Sets a 3×3 matrix uniform from a row-major float[9] array.
    /// Used by the video shader for YUV→RGB colour conversion coefficients.
    /// </summary>
    void SetUniform(string name, float[] mat3X3);

    /// <summary>
    /// Uploads a std140-laid-out uniform block by name. <typeparamref name="T"/> must match the
    /// block's GLSL layout exactly (see <c>Uniforms</c> structs). The backend owns the underlying
    /// buffer and binds it to the block's binding point. Replaces per-name <see cref="SetUniform(string,float)"/>
    /// calls for shaders authored in GLSL 450 with uniform blocks.
    /// </summary>
    void SetUniformBlock<T>(string blockName, in T data) where T : unmanaged;

    /// <summary>
    /// The native program handle. Used by backend-specific code that needs raw access.
    /// </summary>
    nint Handle { get; }
}
